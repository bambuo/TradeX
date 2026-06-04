namespace TradeX.Trading.Risk;

public class RiskSettings
{
    public decimal MaxDailyLoss { get; set; } = 1000;
    public decimal MaxDrawdownPercent { get; set; } = 20;
    public int MaxConsecutiveLosses { get; set; } = 3;
    public int MaxOpenPositions { get; set; } = 10;
    public decimal SlippageTolerance { get; set; } = 0.001m;
    public decimal MaxSlippageAmount { get; set; } = 10;

    /// <summary>
    /// 市价单下单前基于实时订单簿"走簿"估算的最大可接受滑点（百分比）。
    /// 超过即拒绝下单。0 表示禁用此护栏（仅做 quote→base 换算，不拦截）。
    /// </summary>
    public decimal MaxSlippagePercent { get; set; } = 1.0m;
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

    // ─────────────── 持仓级对账（P0-4）───────────────

    /// <summary>是否启用持仓级对账（用交易所余额校验本地开仓量）。</summary>
    public bool PositionReconcileEnabled { get; set; } = true;

    /// <summary>持仓对账周期（秒）。独立于订单对账，默认更稀疏以降低余额接口调用频率。</summary>
    public int PositionReconcileIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// 持仓漂移告警阈值（百分比）。本地开仓量与交易所余额相对偏差超过此值才告警。
    /// </summary>
    public decimal PositionDriftTolerancePercent { get; set; } = 1.0m;

    /// <summary>
    /// 漂移绝对值下限（base 资产数量）。低于此值视为粉尘忽略，避免误报。0 = 不设下限。
    /// </summary>
    public decimal PositionDriftMinAbsolute { get; set; } = 0m;

    /// <summary>
    /// 是否同时上报"交易所余额多于本地持仓"方向的盈余漂移。
    /// 默认 false：仅上报"本地多于实际"（可能卖空头寸的高危方向），降低人工存入等造成的噪声。
    /// </summary>
    public bool PositionDriftReportSurplus { get; set; } = false;

    /// <summary>
    /// 计价（quote）资产清单，用于从交易对名（如 BTCUSDT）切出 base 资产（BTC）。按长度降序匹配后缀。
    /// </summary>
    public List<string> QuoteAssets { get; set; } =
        ["USDT", "USDC", "FDUSD", "TUSD", "BUSD", "DAI", "BTC", "ETH", "BNB", "EUR", "TRY"];

    /// <summary>
    /// 策略评估并行度。每个 trader 在自己的 DI scope（独立 DbContext）中顺序评估其策略；
    /// 多个 trader 的策略组并行执行。0 / 负数 = 取 <c>Environment.ProcessorCount</c>。
    /// 推荐 4-8，过大不会提升吞吐但增加 DB 连接池压力。
    /// </summary>
    public int StrategyEvaluationParallelism { get; set; } = 4;
}
