namespace TradeX.Trading.EventBus;

/// <summary>
/// 通过反射调用 <c>[DomainEventHandler]</c> 标记的方法。
/// 支持任意签名：<c>Method(TPayload payload, Guid traceId, CancellationToken ct)</c>
/// </summary>
internal sealed class MethodEventConsumer(
    Func<string, Guid, CancellationToken, Task> invokeAsync,
    Type eventType) : IEventConsumer
{
    public Type EventType { get; } = eventType;

    public Task ConsumeAsync(string dataJson, Guid traceId, CancellationToken ct)
        => invokeAsync(dataJson, traceId, ct);
}
