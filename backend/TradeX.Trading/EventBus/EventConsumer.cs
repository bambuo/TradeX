using System.Text.Json;

namespace TradeX.Trading.EventBus;

/// <summary>
/// 类型安全的领域事件消费者基类。自动反序列化 JSON 载荷为 <typeparamref name="T"/> 实例。
/// </summary>
internal sealed class EventConsumer<T>(Func<T, Guid, CancellationToken, Task> handler)
    : IEventConsumer
{
    public Type EventType => typeof(T);

    public async Task ConsumeAsync(string dataJson, Guid traceId, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<T>(dataJson, DomainEventBusBase.JsonOptions)
            ?? throw new InvalidOperationException($"无法反序列化事件 {typeof(T).Name} 载荷");
        await handler(payload, traceId, ct);
    }
}
