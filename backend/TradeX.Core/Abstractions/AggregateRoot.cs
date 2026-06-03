using TradeX.Core.Events;

namespace TradeX.Core.Abstractions;

/// <summary>
/// 聚合根基类。
/// 所有聚合根应继承此类以获得领域事件集合与乐观并发支持。
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>聚合根内发生的领域事件集合，由基础设施在保存前清空并发布。</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>向集合追加领域事件。</summary>
    protected void AddDomainEvent(IDomainEvent evt) => _domainEvents.Add(evt);

    /// <summary>清空领域事件集合（由基础设施在发布后调用）。</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
