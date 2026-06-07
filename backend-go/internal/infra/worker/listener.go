package worker

import (
	"context"
	"time"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"

	bt "tradex/internal/domain/backtest"
	"tradex/internal/infra/eventbus"
)

const (
	StreamKey       = "tradex:backtest"
	ConsumerGroup   = "worker-backtest"
	PollDelay       = 500 * time.Millisecond
	ReclaimInterval = 30 * time.Second
	StaleIdleMs     = 60 * time.Second
)

type BacktestTaskListener struct {
	repo  bt.BacktestRepository
	queue TaskQueue
	bus   *eventbus.RedisEventBus
	log   zerolog.Logger
}

func NewTaskListener(repo bt.BacktestRepository, queue TaskQueue, bus *eventbus.RedisEventBus, log zerolog.Logger) *BacktestTaskListener {
	return &BacktestTaskListener{
		repo:  repo,
		queue: queue,
		bus:   bus,
		log:   log,
	}
}

func (l *BacktestTaskListener) Start(ctx context.Context) {
	if l.bus == nil {
		l.log.Warn().Msg("Redis 不可用，跳过流监听器")
		return
	}

	if err := l.bus.EnsureConsumerGroup(ctx, StreamKey, ConsumerGroup); err != nil {
		l.log.Error().Err(err).Msg("确保消费者组失败")
	}

	consumer := eventbus.ConsumerName()

	// 启动时排干 PEL
	l.drainPEL(ctx, consumer)

	// 新消息消费循环
	go l.readLoop(ctx, consumer)
	// PEL 定期回收
	go l.reclaimLoop(ctx, consumer)
}

func (l *BacktestTaskListener) drainPEL(ctx context.Context, consumer string) {
	msgs, err := l.bus.ConsumePending(ctx, StreamKey, ConsumerGroup, consumer, 100)
	if err != nil {
		l.log.Warn().Err(err).Msg("PEL 清理失败，继续执行")
		return
	}
	for _, msg := range msgs {
		l.processMessage(ctx, msg)
	}
	if len(msgs) > 0 {
		l.log.Info().Int("count", len(msgs)).Msg("PEL 清理完成")
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
			l.log.Warn().Err(err).Msg("读取流失败，重试中")
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
				l.log.Warn().Err(err).Msg("PEL 回收失败")
				continue
			}
			for _, msg := range reclaimed {
				l.processMessage(ctx, msg)
			}
			if len(reclaimed) > 0 {
				l.log.Info().Int("count", len(reclaimed)).Msg("PEL 回收完成")
			}
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
		l.log.Warn().Str("entry_id", msg.ID).Msg("流消息缺少 task_id，已确认")
		l.bus.Ack(ctx, StreamKey, group, msg.ID)
		return
	}

	taskID, err := uuid.Parse(raw)
	if err != nil {
		l.log.Warn().Err(err).Str("raw", raw).Msg("无效的 task_id，已确认")
		l.bus.Ack(ctx, StreamKey, group, msg.ID)
		return
	}

	l.log.Info().Str("task_id", taskID.String()).Msg("从流收到任务")

	if err := l.queue.Enqueue(ctx, taskID); err != nil {
		l.log.Error().Err(err).Str("task_id", taskID.String()).Msg("入队失败，留在 PEL")
		return
	}

	l.bus.MarkProcessed(ctx, group, msg.ID)
	l.bus.Ack(ctx, StreamKey, group, msg.ID)
}
