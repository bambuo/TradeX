namespace TradeX.Trading.Risk;

public class RiskSettings
{
    public decimal MaxDailyLoss { get; set; } = 1000;
    public decimal MaxDrawdownPercent { get; set; } = 20;
    public int MaxConsecutiveLosses { get; set; } = 3;
    public int MaxOpenPositions { get; set; } = 10;
    public decimal SlippageTolerance { get; set; } = 0.001m;
    public decimal MaxSlippageAmount { get; set; } = 10;
    public bool CircuitBreakerActive { get; set; }
    public int CooldownSeconds { get; set; } = 300;

    /// <summary>
    /// 单笔订单名义价值上限（quote 币种计价，例如 USDT）。
    /// 0 表示禁用此检查。生产环境建议根据账户规模设置上限。
    /// </summary>
    public decimal MaxOrderNotional { get; set; } = 0;

    /// <summary>OrderReconciler 巡检周期（秒）。Worker 端定期触发。</summary>
    public int OrderReconcileIntervalSeconds { get; set; } = 60;

    /// <summary>Pending 订单的"陈旧"阈值（分钟）。超过此时间未确认的订单会被对账。</summary>
    public int StalePendingMinutes { get; set; } = 5;
}
