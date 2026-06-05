using TradeX.Core.Events;

namespace TradeX.Core.Abstractions;

/// <summary>
/// 领域事件处理器标记接口。
/// 实现类在 SaveChanges 成功后由 <c>DomainEventDispatcher</c> 自动调用。
/// handler 不应执行写操作（事务已提交），只用于触发 side effect（如 push UI、写审计日志）。
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}
