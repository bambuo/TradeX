namespace TradeX.Trading.EventBus;

/// <summary>
/// 内部统一分发的消费者接口。<see cref="EventConsumer{T}"/> 和 <see cref="MethodEventConsumer"/> 
/// 都实现此接口，由 <see cref="EventConsumerService"/> 统一调度。
/// </summary>
internal interface IEventConsumer
{
    Type EventType { get; }
    Task ConsumeAsync(string dataJson, Guid traceId, CancellationToken ct);
}
