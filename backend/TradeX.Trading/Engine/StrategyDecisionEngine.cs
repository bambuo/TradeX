using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;

namespace TradeX.Trading.Engine;

/// <summary>策略决策动作（与"如何兑现"无关，实盘/回测共用）。</summary>
public enum StrategyAction
{
    /// <summary>无动作。</summary>
    Hold,
    /// <summary>条件策略入场（市价买入），由调用方决定下单金额。</summary>
    EnterMarket,
    /// <summary>网格加仓一档（市价买入 <see cref="StrategyDecision.QuoteSize"/>）。</summary>
    AddGridLot,
    /// <summary>网格减仓一档（平掉最早一笔持仓）。</summary>
    ReduceOneLot,
    /// <summary>条件策略出场（平掉该交易对全部持仓）。</summary>
    ExitAll
}

/// <summary>
/// 决策结果。<see cref="QuoteSize"/> 仅对买入类动作有意义：
/// 网格加仓为 BasePositionSize；条件入场为 0，表示由调用方按各自策略定额（实盘固定额 / 回测可用资金）。
/// </summary>
public sealed record StrategyDecision(StrategyAction Action, decimal QuoteSize, string Reason)
{
    public static StrategyDecision Hold(string reason) => new(StrategyAction.Hold, 0m, reason);
    public static StrategyDecision Enter(string reason) => new(StrategyAction.EnterMarket, 0m, reason);
    public static StrategyDecision AddGrid(decimal quoteSize, string reason) => new(StrategyAction.AddGridLot, quoteSize, reason);
    public static StrategyDecision ReduceOne(string reason) => new(StrategyAction.ReduceOneLot, 0m, reason);
    public static StrategyDecision Exit(string reason) => new(StrategyAction.ExitAll, 0m, reason);
}

/// <summary>
/// 决策输入：策略定义（入场/出场/执行规则 JSON）、当前与上一根指标值、当前价、以及聚合后的持仓状态。
/// </summary>
public sealed record StrategyDecisionInput(
    string EntryConditionJson,
    string ExitConditionJson,
    string ExecutionRuleJson,
    Dictionary<string, decimal> IndicatorValues,
    Dictionary<string, decimal> PreviousIndicatorValues,
    decimal CurrentPrice,
    decimal AverageEntryPrice,
    decimal QuantityHeld,
    int LotCount);

/// <summary>
/// 策略决策内核：把"行情 + 持仓状态 → 决策"的纯逻辑从执行细节中剥离，供实盘评估器与回测引擎共用，
/// 保证两路在相同输入下产生相同决策（含波动率网格）。无任何 IO。
/// </summary>
public interface IStrategyDecisionEngine
{
    StrategyDecision Decide(StrategyDecisionInput input);
}

public sealed class StrategyDecisionEngine(
    IConditionEvaluator conditionEvaluator,
    ILogger<StrategyDecisionEngine>? logger = null) : IStrategyDecisionEngine
{
    public StrategyDecision Decide(StrategyDecisionInput input)
    {
        // ═══ 波动率网格：复用纯算法 VolatilityGridExecutor.Decide ═══
        var gridRule = VolatilityGridExecutionRuleParser.TryParse(input.ExecutionRuleJson, logger);
        if (gridRule is not null)
        {
            var state = new VolatilityGridState(input.AverageEntryPrice, input.QuantityHeld, input.LotCount);
            var decision = new VolatilityGridExecutor(gridRule).Decide(state, input.CurrentPrice);
            return decision.Action switch
            {
                VolatilityGridAction.Buy => StrategyDecision.AddGrid(gridRule.BasePositionSize, decision.Reason),
                VolatilityGridAction.Sell => StrategyDecision.ReduceOne(decision.Reason),
                _ => StrategyDecision.Hold(decision.Reason)
            };
        }

        // ═══ 条件策略：入场（无仓）/ 出场（有仓）═══
        var hasPosition = input.QuantityHeld > 0;

        if (!hasPosition && HasCondition(input.EntryConditionJson)
            && conditionEvaluator.Evaluate(input.EntryConditionJson, input.IndicatorValues, input.PreviousIndicatorValues))
            return StrategyDecision.Enter("条件入场");

        if (hasPosition && HasCondition(input.ExitConditionJson)
            && conditionEvaluator.Evaluate(input.ExitConditionJson, input.IndicatorValues, input.PreviousIndicatorValues))
            return StrategyDecision.Exit("条件出场");

        return StrategyDecision.Hold("无信号");
    }

    private static bool HasCondition(string json) => !string.IsNullOrWhiteSpace(json) && json != "{}";
}
