namespace TradeX.Trading.EventBus;

/// <summary>
/// 领域事件总线。传输无关的生产者接口。
/// 消费端通过 <see cref="DomainEventHandlerAttribute"/> 属性驱动，不依赖此接口。
/// </summary>
public interface IDomainEventBus
{
    /// <summary>发布领域事件。内部序列化为 JSON 后推送到底层传输。</summary>
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : notnull;
}
