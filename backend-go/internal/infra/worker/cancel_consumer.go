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
	CancelStreamKey     = "tradex:backtest:cancel"
	CancelConsumerGroup = "worker-backtest-cancel"
	CancelPollDelay     = 50 * time.Millisecond
)

type BacktestCancellationConsumer struct {
	tracker *RunningBacktestTracker
	bus     *eventbus.RedisEventBus
	log     zerolog.Logger
}

func NewCancellationConsumer(tracker *RunningBacktestTracker, bus *eventbus.RedisEventBus, log zerolog.Logger) *BacktestCancellationConsumer {
	return &BacktestCancellationConsumer{
		tracker: tracker,
		bus:     bus,
		log:     log,
	}
}

func (c *BacktestCancellationConsumer) Start(ctx context.Context) {
	if c.bus == nil {
		c.log.Warn().Msg("Redis 不可用，跳过取消消费者")
		return
	}

	if err := c.bus.EnsureConsumerGroup(ctx, CancelStreamKey, CancelConsumerGroup); err != nil {
		c.log.Error().Err(err).Msg("确保取消消费者组失败")
	}

	consumer := eventbus.ConsumerName()

	go c.readLoop(ctx, consumer)
	go c.reclaimLoop(ctx, consumer)
}

func (c *BacktestCancellationConsumer) readLoop(ctx context.Context, consumer string) {
	for {
		select {
		case <-ctx.Done():
			return
		default:
		}

		msgs, err := c.bus.ReadNew(ctx, CancelStreamKey, CancelConsumerGroup, consumer, 10, 0)
		if err != nil {
			if ctx.Err() != nil {
				return
			}
			c.log.Warn().Err(err).Msg("取消流读取失败，重试中")
			time.Sleep(CancelPollDelay)
			continue
		}

		for _, msg := range msgs {
			c.processCancel(ctx, msg)
		}

		if len(msgs) == 0 {
			time.Sleep(CancelPollDelay)
		}
	}
}

func (c *BacktestCancellationConsumer) reclaimLoop(ctx context.Context, consumer string) {
	ticker := time.NewTicker(ReclaimInterval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			reclaimed, err := c.bus.ClaimStale(ctx, CancelStreamKey, CancelConsumerGroup, consumer, StaleIdleMs)
			if err != nil {
				c.log.Warn().Err(err).Msg("取消 PEL 回收失败")
				continue
			}
			for _, msg := range reclaimed {
				c.processCancel(ctx, msg)
			}
		}
	}
}

func (c *BacktestCancellationConsumer) processCancel(ctx context.Context, msg redis.XMessage) {
	group := CancelConsumerGroup

	dup, err := c.bus.IsAlreadyProcessed(ctx, group, msg.ID)
	if err == nil && dup {
		c.bus.Ack(ctx, CancelStreamKey, group, msg.ID)
		return
	}

	raw, ok := eventbus.ParseTaskID(msg)
	if !ok {
		c.log.Warn().Str("entry_id", msg.ID).Msg("取消消息缺少 task_id，已确认")
		c.bus.Ack(ctx, CancelStreamKey, group, msg.ID)
		return
	}

	taskID, err := uuid.Parse(raw)
	if err != nil {
		c.log.Warn().Err(err).Str("raw", raw).Msg("无效的取消 task_id，已确认")
		c.bus.Ack(ctx, CancelStreamKey, group, msg.ID)
		return
	}

	c.log.Info().Str("task_id", taskID.String()).Msg("从流收到取消事件")

	if c.tracker.Cancel(taskID) {
		c.log.Info().Str("task_id", taskID.String()).Interface("event",
			bt.BacktestCancelledEvent{TaskID: taskID, Timestamp: time.Now()}).Msg("回测已取消")
	} else {
		c.log.Warn().Str("task_id", taskID.String()).Msg("任务不在跟踪器中，可能已完成")
	}

	c.bus.MarkProcessed(ctx, group, msg.ID)
	c.bus.Ack(ctx, CancelStreamKey, group, msg.ID)
}
