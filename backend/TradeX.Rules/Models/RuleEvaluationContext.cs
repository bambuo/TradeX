namespace TradeX.Rules.Models;

/// <summary>规则评估上下文。</summary>
/// <param name="ScopeKey">
/// 触发追踪的隔离作用域键（如实盘 "{bindingId}:{pair}"、回测 "backtest"）。
/// 与规则 Code 组合成 MinInterval 冷却的唯一键，避免不同策略/交易对/回测之间互相串扰。
/// </param>
/// <param name="EvaluationTime">
/// 本次评估的时间基准。实盘传 <c>IClock.UtcNow</c>，回测传当前 K 线时间，
/// 使 MinInterval 约束在回测中按模拟时间生效（而非墙钟）。
/// </param>
public sealed record RuleEvaluationContext(
    decimal CurrentPrice,
    decimal AverageEntryPrice,
    decimal QuantityHeld,
    int LotCount,
    Dictionary<string, decimal> IndicatorValues,
    Dictionary<string, decimal>? PreviousIndicatorValues = null,
    string ScopeKey = "",
    DateTime EvaluationTime = default);
