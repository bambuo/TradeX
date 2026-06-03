using TradeX.Core.Abstractions;
using TradeX.Core.Enums;
using TradeX.Core.Events;

namespace TradeX.Core.Models;

public class StrategyBinding : AggregateRoot
{
    // EF Core 无参构造函数
    public StrategyBinding() { }

    /// <summary>工厂方法：创建策略绑定。</summary>
    public static StrategyBinding Create(
        Guid strategyId, string name, Guid traderId,
        Guid exchangeId, string pairs, string timeframe, Guid createdBy)
    {
        return new StrategyBinding
        {
            StrategyId = strategyId,
            Name = name,
            TraderId = traderId,
            ExchangeId = exchangeId,
            Pairs = pairs,
            Timeframe = timeframe,
            CreatedBy = createdBy
        };
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StrategyId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid TraderId { get; init; }
    public Guid ExchangeId { get; set; }
    public string Pairs { get; set; } = "[]";
    public string Timeframe { get; set; } = "15m";
    public BindingStatus Status { get; set; } = BindingStatus.Disabled;
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ─────────────── 领域方法 ───────────────

    /// <summary>激活策略绑定。</summary>
    public void Activate()
    {
        if (Status == BindingStatus.Active) return;
        var old = Status.ToString();
        Status = BindingStatus.Active;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BindingStatusChangedEvent(Id, TraderId, old, Status.ToString()));
    }

    /// <summary>禁用策略绑定。</summary>
    public void Deactivate()
    {
        if (Status == BindingStatus.Disabled) return;
        var old = Status.ToString();
        Status = BindingStatus.Disabled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BindingStatusChangedEvent(Id, TraderId, old, Status.ToString()));
    }
}
