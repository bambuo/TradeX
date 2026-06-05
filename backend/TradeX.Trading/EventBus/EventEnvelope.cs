namespace TradeX.Trading.EventBus;

/// <summary>
/// 事件总线的通用包络。生产者序列化领域事件载荷为 JSON，消费者按 <c>EventType</c> 反序列化。
/// </summary>
public sealed record EventEnvelope(
    string EventType,
    Guid TraceId,
    string DataJson);
