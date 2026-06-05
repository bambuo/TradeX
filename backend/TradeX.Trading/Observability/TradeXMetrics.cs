using System.Diagnostics.Metrics;

namespace TradeX.Trading.Observability;

/// <summary>
/// TradeX 自定义业务指标。Meter 名称 <c>tradex</c>，Prometheus 抓取格式：
/// <c>tradex_orders_placed_total</c>、<c>tradex_orders_rejected_total</c>、
/// <c>tradex_mfa_attempts_total</c>。
/// </summary>
public sealed class TradeXMetrics : IDisposable
{
    public const string MeterName = "tradex";

    private readonly Meter _meter;

    public Counter<long> OrdersPlaced { get; }
    public Counter<long> OrdersRejected { get; }
    public Counter<long> RiskDenials { get; }
    public Counter<long> MfaAttempts { get; }
    public Counter<long> RateLimitHits { get; }
    public Counter<long> KillSwitchActivations { get; }
    public Counter<long> NotificationsFailed { get; }
    public Counter<long> PositionDriftDetected { get; }
    public Histogram<double> StrategyEvalDurationMs { get; }
    public Histogram<double> BacktestRunDurationMs { get; }

    // 由 BacktestScheduler 在每次轮询时调用 SetBacktestQueueDepth, ObservableGauge 读取最新值
    private long _backtestQueueDepth;
    private long _killSwitchActive;

    public TradeXMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        OrdersPlaced = _meter.CreateCounter<long>("tradex.orders.placed", unit: "{order}",
            description: "成功提交至交易所的订单数（按交易所/方向/状态打标签）");
        OrdersRejected = _meter.CreateCounter<long>("tradex.orders.rejected", unit: "{order}",
            description: "下单失败计数（交易所返回错误，按 reason 打标签）");
        RiskDenials = _meter.CreateCounter<long>("tradex.risk.denials", unit: "{denial}",
            description: "风控拒绝计数（按 handler 名打标签, 例: handler=DailyLossHandler）");
        MfaAttempts = _meter.CreateCounter<long>("tradex.mfa.attempts", unit: "{attempt}",
            description: "MFA 二次校验尝试计数（按 result=success/invalid/missing/not_configured 打标签）");
        RateLimitHits = _meter.CreateCounter<long>("tradex.ratelimit.hits", unit: "{hit}",
            description: "交易所限流命中计数（按 exchange 打标签）");
        KillSwitchActivations = _meter.CreateCounter<long>("tradex.killswitch.activations", unit: "{event}",
            description: "Kill Switch 激活次数（按 reason 打标签）");
        NotificationsFailed = _meter.CreateCounter<long>("tradex.notifications.failed", unit: "{event}",
            description: "通知最终失败 (重试耗尽) 计数（按 channel 打标签）");
        PositionDriftDetected = _meter.CreateCounter<long>("tradex.position.drift_detected", unit: "{event}",
            description: "持仓级对账发现漂移超阈值计数（按 exchange/severity 打标签）");

        StrategyEvalDurationMs = _meter.CreateHistogram<double>("tradex.strategy.eval_duration", unit: "ms",
            description: "单个策略评估耗时 (ms)");
        BacktestRunDurationMs = _meter.CreateHistogram<double>("tradex.backtest.run_duration", unit: "ms",
            description: "回测引擎完整执行耗时 (ms), 不含 K 线拉取");

        _meter.CreateObservableGauge("tradex.backtest.queue_depth", () => _backtestQueueDepth,
            description: "BacktestTaskQueue 当前 Pending 任务数");
        _meter.CreateObservableGauge("tradex.killswitch.active", () => _killSwitchActive,
            description: "Kill Switch 当前状态 (0=未激活, 1=已激活)");
    }

    public void SetBacktestQueueDepth(long depth) => Interlocked.Exchange(ref _backtestQueueDepth, depth);
    public void SetKillSwitchActive(bool active) => Interlocked.Exchange(ref _killSwitchActive, active ? 1L : 0L);

    public void Dispose() => _meter.Dispose();
}
