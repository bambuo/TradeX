using System.Text.Json;

namespace TradeX.Trading.EventBus;

/// <summary>
/// 事件总线模板方法基类。
/// 子类只需实现 <see cref="PublishCoreAsync"/> 将序列化后的字符串推送到具体传输（Redis / MQTT 等）。
/// </summary>
public abstract class DomainEventBusBase : IDomainEventBus
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : notnull
    {
        var eventType = @event.GetType().FullName!;
        var traceId = Guid.NewGuid();
        var dataJson = JsonSerializer.Serialize(@event, JsonOptions);
        var envelope = new EventEnvelope(eventType, traceId, dataJson);

        await PublishCoreAsync(envelope, ct);
    }

    /// <summary>由子类实现：将包络推送到底层传输。</summary>
    protected abstract Task PublishCoreAsync(EventEnvelope envelope, CancellationToken ct);
}
