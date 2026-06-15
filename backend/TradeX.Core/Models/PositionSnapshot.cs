namespace TradeX.Core.Models;

/// <summary>持仓快照（只读聚合视图，用于规则链评估）。</summary>
public sealed class PositionSnapshot
{
    /// <summary>持仓数量（正=多、负=空）。</summary>
    public decimal Quantity { get; set; }

    /// <summary>平均开仓价。</summary>
    public decimal EntryPrice { get; set; }

    /// <summary>当前市价。</summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>加仓次数/层数。</summary>
    public int LotCount { get; set; }

    /// <summary>未实现盈亏（currentPrice - entryPrice）* quantity。</summary>
    public decimal UnrealizedPnl { get; set; }

    /// <summary>市场类型。</summary>
    public Enums.MarketType MarketType { get; set; }

    /// <summary>持仓方向。</summary>
    public Enums.PositionSide PositionSide { get; set; }

    /// <summary>杠杆倍数。</summary>
    public decimal Leverage { get; set; }

    /// <summary>保证金类型。</summary>
    public Enums.MarginType MarginType { get; set; }

    /// <summary>强平价格。</summary>
    public decimal LiquidationPrice { get; set; }

    /// <summary>是否有实际持仓。</summary>
    public bool HasPosition() => Quantity != decimal.Zero;
}

/// <summary>组合快照（只读聚合视图，用于规则链评估）。</summary>
public sealed class PortfolioSnapshot
{
    /// <summary>总权益（现金 + 持仓权益贡献）。</summary>
    public decimal TotalEquity { get; set; }

    /// <summary>可用现金（计价币余额）。</summary>
    public decimal AvailableCash { get; set; }

    /// <summary>当前持仓数。</summary>
    public int OpenPositions { get; set; }

    /// <summary>当日已实现盈亏。</summary>
    public decimal DailyPnl { get; set; }

    /// <summary>当日回撤百分比（0-100）。</summary>
    public decimal Drawdown { get; set; }
}
