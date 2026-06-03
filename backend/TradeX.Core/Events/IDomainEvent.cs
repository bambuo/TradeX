namespace TradeX.Core.Events;

/// <summary>
/// 领域事件标记接口。
/// 聚合根内的领域方法应追加领域事件到 <see cref="Abstractions.AggregateRoot.DomainEvents"/> 集合。
/// 基础设施层在 SaveChanges 前自动清空并发布。
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}
