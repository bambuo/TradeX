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
        Guid exchangeId, string pairs, string timeframe,
        MarketType marketType, Guid createdBy)
    {
        return new StrategyBinding
        {
            StrategyId = strategyId,
            Name = name,
            TraderId = traderId,
            ExchangeId = exchangeId,
            Pairs = pairs,
            Timeframe = timeframe,
            MarketType = marketType,
            CreatedBy = createdBy
        };
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StrategyId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid TraderId { get; init; }
    public Guid ExchangeId { get; set; }
    public MarketType MarketType { get; set; } = MarketType.Spot;
    public string Pairs { get; set; } = "[]";
    public string Timeframe { get; set; } = "15m";
    public BindingStatus Status { get; set; } = BindingStatus.Disabled;
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ─────────── 合约专属字段（MarketType == Perpetual 时生效）───────────

    /// <summary>持仓方向：Long / Short / Both。</summary>
    public PositionMode PositionMode { get; set; } = PositionMode.Both;

    /// <summary>保证金类型：Cross / Isolated。</summary>
    public MarginType MarginType { get; set; } = MarginType.Isolated;

    /// <summary>杠杆倍数。</summary>
    public decimal Leverage { get; set; } = 1;

    // ─────────────── 领域方法 ───────────────

    /// <summary>激活策略绑定。</summary>
    public void Activate()
    {
        if (Status == BindingStatus.Active) return;
        var old = Status.ToString();
        Status = BindingStatus.Active;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BindingStatusChangedDomainEvent(Id, TraderId, old, Status.ToString()));
    }

    /// <summary>禁用策略绑定。</summary>
    public void Deactivate()
    {
        if (Status == BindingStatus.Disabled) return;
        var old = Status.ToString();
        Status = BindingStatus.Disabled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BindingStatusChangedDomainEvent(Id, TraderId, old, Status.ToString()));
    }

    /// <summary>是否为活跃状态。</summary>
    public bool IsActive() => Status == BindingStatus.Active;

    /// <summary>把逗号分隔的 Pairs 切成交易对列表。</summary>
    public string[] PairList() =>
        Pairs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>返回有效的 K 线周期，空则回退到 "15m"。</summary>
    public string EffectiveTimeframe() =>
        string.IsNullOrWhiteSpace(Timeframe) ? "15m" : Timeframe;
}
