using Microsoft.Extensions.Logging;
using TradeX.Rules.Engine;
using TradeX.Rules.Models;
using TradeX.Rules.Parsers;

namespace TradeX.Trading.Engine;

/// <summary>策略决策动作（与"如何兑现"无关，实盘/回测共用）。</summary>
public enum StrategyAction
{
    /// <summary>无动作。</summary>
    Hold,
    /// <summary>策略买入（市价），下单金额见 <see cref="StrategyDecision.QuoteSize"/>；持仓中买入即加仓。</summary>
    EnterMarket,
    /// <summary>
    /// 减仓。<see cref="StrategyDecision.QuoteSize"/> &gt; 0 时按该 quote 金额自最早持仓起逐笔平仓
    /// （整笔 lot 粒度，累计名义价值达到金额即止）；为 0 时平掉最早一笔持仓。
    /// </summary>
    Reduce,
    /// <summary>策略出场（平掉该交易对全部持仓）。</summary>
    ExitAll
}

/// <summary>
/// 决策结果。<see cref="QuoteSize"/> 对买入类动作表示下单金额；对 <see cref="StrategyAction.Reduce"/>
/// 表示目标减仓金额（0 表示减一笔）。
/// </summary>
public sealed record StrategyDecision(StrategyAction Action, decimal QuoteSize, string Reason)
{
    public static StrategyDecision Hold(string reason) => new(StrategyAction.Hold, 0m, reason);
    public static StrategyDecision Enter(string reason) => new(StrategyAction.EnterMarket, 0m, reason);
    public static StrategyDecision Reduce(decimal quoteSize, string reason) => new(StrategyAction.Reduce, quoteSize, reason);
    public static StrategyDecision Exit(string reason) => new(StrategyAction.ExitAll, 0m, reason);
}

/// <summary>
/// 决策输入：执行规则 JSON、当前与上一根指标值、当前价、聚合后的持仓状态，
/// 以及触发追踪所需的作用域键与评估时间基准。
/// </summary>
public sealed record StrategyDecisionInput(
    string ExecutionRule,
    Dictionary<string, decimal> IndicatorValues,
    Dictionary<string, decimal> PreviousIndicatorValues,
    decimal CurrentPrice,
    decimal AverageEntryPrice,
    decimal QuantityHeld,
    int LotCount,
    string ScopeKey,
    DateTime EvaluationTime);

/// <summary>
/// 策略决策内核：将规则集 JSON → RuleEvaluator，把"行情 + 持仓状态 → 决策"的纯逻辑
/// 从执行细节中剥离，供实盘评估器与回测引擎共用。无任何 IO。
/// </summary>
public interface IStrategyDecisionEngine
{
    StrategyDecision Decide(StrategyDecisionInput input);
}

public sealed class StrategyDecisionEngine(
    IRuleEvaluator ruleEvaluator,
    ILogger<StrategyDecisionEngine>? logger = null) : IStrategyDecisionEngine
{
    public StrategyDecision Decide(StrategyDecisionInput input)
    {
        // 1. 解析 ExecutionRule 为统一规则集（fail-closed：解析失败 → 无决策）
        var ruleSet = RuleSetParser.TryParse(input.ExecutionRule, logger);

        if (ruleSet is null || ruleSet.Rules.Count == 0)
            return StrategyDecision.Hold("无规则集或解析失败");

        // 2. 构建评估上下文
        var ctx = new RuleEvaluationContext(
            input.CurrentPrice,
            input.AverageEntryPrice,
            input.QuantityHeld,
            input.LotCount,
            input.IndicatorValues,
            input.PreviousIndicatorValues,
            input.ScopeKey,
            input.EvaluationTime);

        // 3. 评估
        var decision = ruleEvaluator.Evaluate(ruleSet, ctx);

        // 4. 将 RuleDecision 映射为 StrategyDecision
        logger?.LogInformation("规则集 {Code}({Name}) → {Action}, Size={Size}, Reason={Reason}",
            ruleSet.Code, ruleSet.Name, decision.Action, decision.Size, decision.Reason);

        return decision.Action switch
        {
            RuleDecisionAction.Buy => new StrategyDecision(StrategyAction.EnterMarket, decision.Size, decision.Reason ?? "规则触发买入"),
            RuleDecisionAction.Sell => new StrategyDecision(StrategyAction.Reduce, decision.Size, decision.Reason ?? "规则触发减仓"),
            RuleDecisionAction.SellAll => StrategyDecision.Exit(decision.Reason ?? "规则触发全部平仓"),
            _ => StrategyDecision.Hold(decision.Reason ?? "无规则触发")
        };
    }
}
