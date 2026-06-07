package eventbus

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"strings"
	"time"

	"github.com/redis/go-redis/v9"
)

const (
	DedupTTL = 10 * time.Minute
	MaxLen   = 10000
)

var hostname string

func init() {
	hostname, _ = os.Hostname()
	if hostname == "" {
		hostname = "unknown"
	}
}

func ConsumerName() string {
	return hostname
}

type RedisEventBus struct {
	client *redis.Client
}

func NewRedisEventBus(client *redis.Client) *RedisEventBus {
	return &RedisEventBus{client: client}
}

func (r *RedisEventBus) Client() *redis.Client {
	return r.client
}

func (r *RedisEventBus) Publish(ctx context.Context, channel string, payload any) error {
	data, err := json.Marshal(payload)
	if err != nil {
		return err
	}
	return r.client.Publish(ctx, channel, data).Err()
}

func (r *RedisEventBus) Subscribe(ctx context.Context, channel string, handler func(ctx context.Context, payload any)) error {
	pubsub := r.client.Subscribe(ctx, channel)
	defer pubsub.Close()

	ch := pubsub.Channel()
	for {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case msg, ok := <-ch:
			if !ok {
				return nil
			}
			var payload any
			if err := json.Unmarshal([]byte(msg.Payload), &payload); err == nil {
				handler(ctx, payload)
			}
		}
	}
}

func (r *RedisEventBus) Close() error {
	return r.client.Close()
}

// ─── Stream 操作 ─────────────────────────────────────────

func (r *RedisEventBus) StreamAdd(ctx context.Context, stream string, values map[string]any) error {
	return r.client.XAdd(ctx, &redis.XAddArgs{
		Stream: stream,
		Values: values,
		MaxLen: MaxLen,
		Approx: true,
	}).Err()
}

func (r *RedisEventBus) EnsureConsumerGroup(ctx context.Context, stream, group string) error {
	err := r.client.XGroupCreateMkStream(ctx, stream, group, "0").Err()
	if err != nil {
		if strings.Contains(err.Error(), "BUSYGROUP") {
			return nil
		}
	}
	return err
}

func (r *RedisEventBus) IsAlreadyProcessed(ctx context.Context, group, entryID string) (bool, error) {
	key := dedupKey(group, entryID)
	n, err := r.client.Exists(ctx, key).Result()
	return n > 0, err
}

func (r *RedisEventBus) MarkProcessed(ctx context.Context, group, entryID string) error {
	key := dedupKey(group, entryID)
	return r.client.Set(ctx, key, "1", DedupTTL).Err()
}

// ─── PEL 管理 ────────────────────────────────────────────

func (r *RedisEventBus) ClaimStale(ctx context.Context, stream, group, consumer string, minIdle time.Duration) ([]redis.XMessage, error) {
	msgs, _, err := r.client.XAutoClaim(ctx, &redis.XAutoClaimArgs{
		Stream:   stream,
		Group:    group,
		Consumer: consumer,
		MinIdle:  minIdle,
		Start:    "0-0",
		Count:    50,
	}).Result()
	return msgs, err
}

func (r *RedisEventBus) ConsumePending(ctx context.Context, stream, group, consumer string, count int64) ([]redis.XMessage, error) {
	msgs, err := r.client.XReadGroup(ctx, &redis.XReadGroupArgs{
		Group:    group,
		Consumer: consumer,
		Streams:  []string{stream, "0"},
		Count:    count,
	}).Result()
	if err != nil || len(msgs) == 0 {
		return nil, err
	}
	return msgs[0].Messages, nil
}

func (r *RedisEventBus) ReadNew(ctx context.Context, stream, group, consumer string, count int64, block time.Duration) ([]redis.XMessage, error) {
	msgs, err := r.client.XReadGroup(ctx, &redis.XReadGroupArgs{
		Group:    group,
		Consumer: consumer,
		Streams:  []string{stream, ">"},
		Count:    count,
		Block:    block,
	}).Result()
	if err != nil || len(msgs) == 0 {
		return nil, err
	}
	return msgs[0].Messages, nil
}

func (r *RedisEventBus) Ack(ctx context.Context, stream, group string, ids ...string) error {
	return r.client.XAck(ctx, stream, group, ids...).Err()
}

// ─── StreamMsg 解析 ──────────────────────────────────────

func ParseTaskID(msg redis.XMessage) (string, bool) {
	if raw, ok := msg.Values["task_id"]; ok {
		return fmt.Sprintf("%v", raw), true
	}
	return "", false
}

// RedisCancelNotifier implements domain.CancelNotifier via Redis Stream.
type RedisCancelNotifier struct {
	bus *RedisEventBus
}

func NewRedisCancelNotifier(bus *RedisEventBus) *RedisCancelNotifier {
	return &RedisCancelNotifier{bus: bus}
}

func (n *RedisCancelNotifier) NotifyCancel(ctx context.Context, taskID string) error {
	return n.bus.StreamAdd(ctx, "tradex:backtest:cancel", map[string]any{"task_id": taskID})
}

func dedupKey(group, entryID string) string {
	return fmt.Sprintf("tradex:dedup:%s:%s", group, entryID)
}
