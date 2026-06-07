package domain

// DomainEvent 是可发布的领域事件。
// EventType 返回与 C# 完全一致的类型全名（如 "TradeX.Trading.Events.OrphanOrderDetectedPayload"），
// 因为消费端 RedisEventConsumerService 按 envelope.eventType 派发。
type DomainEvent interface {
	EventType() string
}
