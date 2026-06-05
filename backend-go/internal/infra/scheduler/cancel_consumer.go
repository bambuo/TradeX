package scheduler

import (
	"context"
	"time"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"

	"github.com/tradex/backend-go/internal/domain"
	"github.com/tradex/backend-go/internal/infra/eventbus"
)

const (
	CancelStreamKey     = "tradex:backtest:cancel"
	CancelConsumerGroup = "worker-backtest-cancel"
	CancelPollDelay     = 50 * time.Millisecond
)

type BacktestCancellationConsumer struct {
	tracker *RunningBacktestTracker
	bus     *eventbus.RedisEventBus
	rdb     *redis.Client
	log     zerolog.Logger
}

func NewCancellationConsumer(tracker *RunningBacktestTracker, bus *eventbus.RedisEventBus, log zerolog.Logger) *BacktestCancellationConsumer {
	return &BacktestCancellationConsumer{
		tracker: tracker,
		bus:     bus,
		rdb:     bus.Client(),
		log:     log,
	}
}

func (c *BacktestCancellationConsumer) Start(ctx context.Context) {
	if c.bus == nil {
		c.log.Warn().Msg("redis bus not available, skipping cancel consumer")
		return
	}

	if err := c.bus.EnsureConsumerGroup(ctx, CancelStreamKey, CancelConsumerGroup); err != nil {
		c.log.Error().Err(err).Msg("failed to ensure cancel consumer group")
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
			c.log.Warn().Err(err).Msg("cancel stream read failed, retrying")
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
				c.log.Warn().Err(err).Msg("cancel PEL reclaim failed")
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
		c.log.Warn().Str("entry_id", msg.ID).Msg("cancel msg missing task_id, acking")
		c.bus.Ack(ctx, CancelStreamKey, group, msg.ID)
		return
	}

	taskID, err := uuid.Parse(raw)
	if err != nil {
		c.log.Warn().Err(err).Str("raw", raw).Msg("invalid cancel task_id, acking")
		c.bus.Ack(ctx, CancelStreamKey, group, msg.ID)
		return
	}

	c.log.Info().Str("task_id", taskID.String()).Msg("received cancel from stream")

	if c.tracker.Cancel(taskID) {
		c.log.Info().Str("task_id", taskID.String()).Interface("event",
			domain.BacktestCancelledEvent{TaskID: taskID, Timestamp: time.Now()}).Msg("backtest_cancelled")
	} else {
		c.log.Warn().Str("task_id", taskID.String()).Msg("task not found in tracker, may already be done")
	}

	c.bus.MarkProcessed(ctx, group, msg.ID)
	c.bus.Ack(ctx, CancelStreamKey, group, msg.ID)
}
