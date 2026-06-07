package trading

import (
	"context"
	"encoding/json"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"

	"tradex/internal/domain"
)

// DomainEventBus 领域事件总线（生产者）。对应 C# IDomainEventBus。
type DomainEventBus interface {
	Publish(ctx context.Context, evt domain.DomainEvent) error
}

// eventEnvelope 与 C# EventEnvelope 一致（camelCase）：消费端按 eventType 反序列化 dataJson。
type eventEnvelope struct {
	EventType string    `json:"eventType"`
	TraceID   uuid.UUID `json:"traceId"`
	DataJSON  string    `json:"dataJson"`
}

// NullDomainEventBus 无操作降级实现（无 Redis 时使用）。
type NullDomainEventBus struct{}

func (NullDomainEventBus) Publish(context.Context, domain.DomainEvent) error { return nil }

const (
	eventsStreamKey    = "tradex:events"
	eventsPayloadKey   = "task_id" // 与 C# RedisStreamHelpers.PayloadField 一致
	eventsStreamMaxLen = 10_000
)

// RedisDomainEventBus 用 Redis Stream（XADD 到 tradex:events）发布领域事件。
// 包络与字段名严格对齐 C#，保证 C# API 端消费者可解析。
type RedisDomainEventBus struct {
	rdb *redis.Client
}

// NewRedisDomainEventBus 构造 Redis 领域事件总线。
func NewRedisDomainEventBus(rdb *redis.Client) *RedisDomainEventBus {
	return &RedisDomainEventBus{rdb: rdb}
}

func (b *RedisDomainEventBus) Publish(ctx context.Context, evt domain.DomainEvent) error {
	dataJSON, err := json.Marshal(evt)
	if err != nil {
		return err
	}
	env := eventEnvelope{
		EventType: evt.EventType(),
		TraceID:   uuid.New(),
		DataJSON:  string(dataJSON),
	}
	payload, err := json.Marshal(env)
	if err != nil {
		return err
	}
	return b.rdb.XAdd(ctx, &redis.XAddArgs{
		Stream: eventsStreamKey,
		MaxLen: eventsStreamMaxLen,
		Approx: true,
		Values: map[string]any{eventsPayloadKey: string(payload)},
	}).Err()
}
