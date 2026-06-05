namespace TradeX.Trading.EventBus;

/// <summary>
/// Null Object 模式的领域事件总线。所有发布操作无操作。
/// 在非 Worker 进程或不使用事件总线的场景下作为安全降级。
/// </summary>
public sealed class NullDomainEventBus : IDomainEventBus
{
    public Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : notnull
        => Task.CompletedTask;
}
