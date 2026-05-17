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

    /// <summary>
    /// 策略评估并行度。每个 trader 在自己的 DI scope（独立 DbContext）中顺序评估其策略；
    /// 多个 trader 的策略组并行执行。0 / 负数 = 取 <c>Environment.ProcessorCount</c>。
    /// 推荐 4-8，过大不会提升吞吐但增加 DB 连接池压力。
    /// </summary>
    public int StrategyEvaluationParallelism { get; set; } = 4;
}
