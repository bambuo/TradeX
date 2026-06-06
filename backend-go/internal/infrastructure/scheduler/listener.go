package scheduler

import (
	"context"
	"time"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"

	"tradex/internal/domain"
	"tradex/internal/infrastructure/eventbus"
)

const (
	StreamKey          = "tradex:backtest"
	ConsumerGroup      = "worker-backtest"
	PollDelay          = 500 * time.Millisecond
	ReclaimInterval    = 30 * time.Second
	StaleIdleMs        = 60 * time.Second
	SafetyNetInterval  = 5 * time.Minute
)

type BacktestTaskListener struct {
	repo   domain.BacktestRepository
	queue  TaskQueue
	bus    *eventbus.RedisEventBus
	rdb    *redis.Client
	log    zerolog.Logger
}

func NewTaskListener(repo domain.BacktestRepository, queue TaskQueue, bus *eventbus.RedisEventBus, log zerolog.Logger) *BacktestTaskListener {
	return &BacktestTaskListener{
		repo:  repo,
		queue: queue,
		bus:   bus,
		rdb:   bus.Client(),
		log:   log,
	}
}

func (l *BacktestTaskListener) Start(ctx context.Context) {
	if l.bus == nil {
		l.log.Warn().Msg("redis bus not available, skipping stream listener")
		return
	}

	if err := l.bus.EnsureConsumerGroup(ctx, StreamKey, ConsumerGroup); err != nil {
		l.log.Error().Err(err).Msg("failed to ensure consumer group")
	}

	consumer := eventbus.ConsumerName()

	// 启动时排干 PEL
	l.drainPEL(ctx, consumer)
	// 启动时 DB 兜底
	l.safetyNetDrain(ctx)

	// 新消息消费循环
	go l.readLoop(ctx, consumer)
	// PEL 定期回收
	go l.reclaimLoop(ctx, consumer)
	// DB 兜底循环
	go l.safetyNetLoop(ctx)
}

func (l *BacktestTaskListener) drainPEL(ctx context.Context, consumer string) {
	msgs, err := l.bus.ConsumePending(ctx, StreamKey, ConsumerGroup, consumer, 100)
	if err != nil {
		l.log.Warn().Err(err).Msg("PEL drain failed, continuing")
		return
	}
	for _, msg := range msgs {
		l.processMessage(ctx, msg)
	}
	if len(msgs) > 0 {
		l.log.Info().Int("count", len(msgs)).Msg("PEL drain completed")
	}
}

func (l *BacktestTaskListener) readLoop(ctx context.Context, consumer string) {
	for {
		select {
		case <-ctx.Done():
			return
		default:
		}

		msgs, err := l.bus.ReadNew(ctx, StreamKey, ConsumerGroup, consumer, 10, 0)
		if err != nil {
			if ctx.Err() != nil {
				return
			}
			l.log.Warn().Err(err).Msg("stream read failed, retrying")
			time.Sleep(PollDelay)
			continue
		}

		for _, msg := range msgs {
			l.processMessage(ctx, msg)
		}

		if len(msgs) == 0 {
			time.Sleep(PollDelay)
		}
	}
}

func (l *BacktestTaskListener) reclaimLoop(ctx context.Context, consumer string) {
	ticker := time.NewTicker(ReclaimInterval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			reclaimed, err := l.bus.ClaimStale(ctx, StreamKey, ConsumerGroup, consumer, StaleIdleMs)
			if err != nil {
				l.log.Warn().Err(err).Msg("PEL reclaim failed")
				continue
			}
			for _, msg := range reclaimed {
				l.processMessage(ctx, msg)
			}
			if len(reclaimed) > 0 {
				l.log.Info().Int("count", len(reclaimed)).Msg("PEL reclaim completed")
			}
		}
	}
}

func (l *BacktestTaskListener) safetyNetDrain(ctx context.Context) {
	tasks, err := l.repo.GetPendingTasks(ctx)
	if err != nil {
		l.log.Warn().Err(err).Msg("safety net drain failed")
		return
	}
	for _, task := range tasks {
		l.log.Info().Str("task_id", task.ID.String()).Msg("safety net enqueue")
		l.queue.Enqueue(ctx, task.ID)
	}
}

func (l *BacktestTaskListener) safetyNetLoop(ctx context.Context) {
	ticker := time.NewTicker(SafetyNetInterval)
	defer ticker.Stop()

	// 首次立即执行
	l.safetyNetDrain(ctx)

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			l.safetyNetDrain(ctx)
		}
	}
}

func (l *BacktestTaskListener) processMessage(ctx context.Context, msg redis.XMessage) {
	group := ConsumerGroup

	// 去重检查
	dup, err := l.bus.IsAlreadyProcessed(ctx, group, msg.ID)
	if err == nil && dup {
		l.bus.Ack(ctx, StreamKey, group, msg.ID)
		return
	}

	raw, ok := eventbus.ParseTaskID(msg)
	if !ok {
		l.log.Warn().Str("entry_id", msg.ID).Msg("missing task_id in stream msg, acking")
		l.bus.Ack(ctx, StreamKey, group, msg.ID)
		return
	}

	taskID, err := uuid.Parse(raw)
	if err != nil {
		l.log.Warn().Err(err).Str("raw", raw).Msg("invalid task_id, acking")
		l.bus.Ack(ctx, StreamKey, group, msg.ID)
		return
	}

	l.log.Info().Str("task_id", taskID.String()).Msg("received task from stream")

	if err := l.queue.Enqueue(ctx, taskID); err != nil {
		l.log.Error().Err(err).Str("task_id", taskID.String()).Msg("enqueue failed, leaving in PEL")
		return
	}

	l.bus.MarkProcessed(ctx, group, msg.ID)
	l.bus.Ack(ctx, StreamKey, group, msg.ID)
}
