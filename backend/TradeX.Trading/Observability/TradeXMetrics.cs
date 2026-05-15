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

    public TradeXMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        OrdersPlaced = _meter.CreateCounter<long>(
            "tradex.orders.placed",
            unit: "{order}",
            description: "成功提交至交易所的订单数（按交易所/方向/状态打标签）");

        OrdersRejected = _meter.CreateCounter<long>(
            "tradex.orders.rejected",
            unit: "{order}",
            description: "下单失败计数（交易所返回错误，按 reason 打标签）");

        RiskDenials = _meter.CreateCounter<long>(
            "tradex.risk.denials",
            unit: "{denial}",
            description: "风控拒绝计数（按拒绝来源 RiskCheck/PairRisk 打标签）");

        MfaAttempts = _meter.CreateCounter<long>(
            "tradex.mfa.attempts",
            unit: "{attempt}",
            description: "MFA 二次校验尝试计数（按 result=success/invalid/missing/not_configured 打标签）");
    }

    public void Dispose() => _meter.Dispose();
}
