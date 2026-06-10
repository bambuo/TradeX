using Microsoft.Extensions.Logging;
using TradeX.Core.Models;
using TradeX.Rules.Indicators;
using TradeX.Rules.Models;

namespace TradeX.Rules.Engine;

public interface IRuleEvaluator
{
    RuleDecision Evaluate(RuleSet ruleSet, RuleEvaluationContext ctx);
}

public sealed class RuleEvaluator(
    ITriggerTracker triggerTracker,
    ILogger<RuleEvaluator>? logger = null) : IRuleEvaluator
{

    public RuleDecision Evaluate(RuleSet ruleSet, RuleEvaluationContext ctx)
    {
        // 1. 计算上下文指标并合并
        var mergedIndicators = MergeIndicators(ctx);
        var hasPosition = ctx.QuantityHeld > 0;

        // 2. 按优先级（数字越小越优先）遍历规则
        var orderedRules = ruleSet.Rules.OrderBy(r => r.Priority);

        foreach (var rule in orderedRules)
        {
            // 上下文检查
            if (!IsContextMatch(rule.Context, hasPosition))
                continue;

            // 约束检查
            if (rule.Constraints is not null && !CheckConstraints(rule.Constraints, rule.Code, ctx))
            {
                logger?.LogDebug("规则 {Code} 未通过约束检查", rule.Code);
                continue;
            }

            // 条件评估（传入历史快照以支持 lookback）
            if (!EvaluateCondition(rule.When, mergedIndicators, ctx.PreviousIndicatorValues, ctx.HistoricalSnapshots))
                continue;

            // 条件满足，执行动作
            logger?.LogInformation("规则 {Code}({Name}) 触发", rule.Code, rule.Name);
            triggerTracker.RecordTrigger(TriggerKey(ctx.ScopeKey, rule.Code), ctx.EvaluationTime);
            return ExecuteAction(rule, mergedIndicators);
        }

        return new RuleDecision(RuleDecisionAction.Hold, Reason: "无规则触发");
    }

    private static string TriggerKey(string scopeKey, string ruleCode) => $"{scopeKey}/{ruleCode}";

    private Dictionary<string, decimal> MergeIndicators(RuleEvaluationContext ctx)
    {
        var merged = new Dictionary<string, decimal>(ctx.IndicatorValues);

        var contextIndicators = ContextIndicatorCalculator.Calculate(
            ctx.CurrentPrice, ctx.AverageEntryPrice, ctx.QuantityHeld, ctx.LotCount);

        foreach (var (key, value) in contextIndicators)
        {
            // 上下文指标优先，覆盖同名技术指标
            merged[key] = value;
        }

        return merged;
    }

    private static bool IsContextMatch(RuleContext context, bool hasPosition)
    {
        return context switch
        {
            RuleContext.Any => true,
            RuleContext.NoPosition => !hasPosition,
            RuleContext.HasPosition => hasPosition,
            _ => false
        };
    }

    private bool CheckConstraints(RuleConstraints constraints, string ruleCode, RuleEvaluationContext ctx)
    {
        if (constraints.MaxPositionValue.HasValue)
        {
            var notional = ctx.QuantityHeld * ctx.CurrentPrice;
            if (notional >= constraints.MaxPositionValue.Value)
                return false;
        }

        if (constraints.MaxPositions.HasValue && ctx.LotCount >= constraints.MaxPositions.Value)
            return false;

        if (constraints.MinInterval.HasValue)
        {
            var elapsed = triggerTracker.ElapsedSecondsSinceLastTrigger(
                TriggerKey(ctx.ScopeKey, ruleCode), ctx.EvaluationTime);
            if (elapsed.HasValue && elapsed.Value < constraints.MinInterval.Value)
            {
                logger?.LogTrace("规则 {Code} 距上次触发仅 {Elapsed:F1}s，未达最小间隔 {MinInterval}s",
                    ruleCode, elapsed.Value, constraints.MinInterval.Value);
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateCondition(
        ConditionNode? condition,
        Dictionary<string, decimal> indicatorValues,
        Dictionary<string, decimal>? previousValues,
        IReadOnlyList<Dictionary<string, decimal>>? historicalSnapshots = null)
    {
        // null 条件视为恒真（如首次入场规则）
        if (condition is null)
            return true;

        return EvaluateNode(condition, indicatorValues, previousValues ?? [], historicalSnapshots);
    }

    private static bool EvaluateNode(
        ConditionNode node,
        Dictionary<string, decimal> indicatorValues,
        Dictionary<string, decimal> previousValues,
        IReadOnlyList<Dictionary<string, decimal>>? historicalSnapshots = null)
    {
        // TRUE 恒真运算符——无论嵌套在条件树何处都返回 true
        if (node.Operator == "TRUE")
            return true;

        // 无子条件视为叶子节点（兼容 JSON 解析默认 Operator="AND" 但无 conditions 的场景）
        if (node.Conditions.Count == 0)
            return EvaluateLeaf(node, indicatorValues, previousValues, historicalSnapshots);

        return node.Operator switch
        {
            "AND" => node.Conditions.All(c => EvaluateNode(c, indicatorValues, previousValues, historicalSnapshots)),
            "OR" => node.Conditions.Any(c => EvaluateNode(c, indicatorValues, previousValues, historicalSnapshots)),
            "NOT" => node.Conditions.Count == 1 && !EvaluateNode(node.Conditions[0], indicatorValues, previousValues, historicalSnapshots),
            _ => EvaluateLeaf(node, indicatorValues, previousValues, historicalSnapshots)
        };
    }

    private static bool EvaluateLeaf(
        ConditionNode leaf,
        Dictionary<string, decimal> indicatorValues,
        Dictionary<string, decimal> previousValues,
        IReadOnlyList<Dictionary<string, decimal>>? historicalSnapshots = null)
    {
        if (leaf.Indicator is null || leaf.Comparison is null)
            return false;

        // lookback 支持：从历史快照中取值，而非当前指标值
        // 仅对简单比较生效，穿越（CA/CB）忽略 lookback。
        decimal actual;
        if (leaf.Lookback.HasValue && leaf.Lookback.Value > 0)
        {
            if (!TryResolveLookback(leaf, indicatorValues, historicalSnapshots, out actual))
                return false;
        }
        else
        {
            if (!indicatorValues.TryGetValue(leaf.Indicator, out actual))
                return false;
        }

        // 比较基准：
        //   * 带 ref（相对比较）→ compared = indicators[ref] * (value ?? 1)，缺失 ref 值则无法比较 → false。
        //   * 无 ref（绝对比较）→ compared = value；缺失 value 则无法比较 → false（不再默认 0 造成"> 0 恒真"）。
        if (!TryResolveCompareValue(leaf, indicatorValues, out var compared))
            return false;

        // 穿越判定的 prev 端基准须用"上一根"的参照值，否则金叉/死叉会在错误时机触发或漏触发。
        // lookback 模式下 CA/CB 直接用当前（含 lookback 已计算）与上一次评估对比。
        var prevHasValue = previousValues.TryGetValue(leaf.Indicator, out var prev);
        var prevHasCompare = TryResolveCompareValue(leaf, previousValues, out var prevCompared);

        return leaf.Comparison switch
        {
            ">" => actual > compared,
            "<" => actual < compared,
            ">=" => actual >= compared,
            "<=" => actual <= compared,
            "==" => Math.Abs(actual - compared) < 0.0001m,
            "CA" => prevHasValue && prevHasCompare && prev <= prevCompared && actual > compared,
            "CB" => prevHasValue && prevHasCompare && prev >= prevCompared && actual < compared,
            _ => false
        };
    }

    /// <summary>按 lookback 从历史快照中解析指标值。索引不足时返回 false。</summary>
    private static bool TryResolveLookback(
        ConditionNode leaf,
        Dictionary<string, decimal> indicatorValues,
        IReadOnlyList<Dictionary<string, decimal>>? historicalSnapshots,
        out decimal actual)
    {
        // lookback=1 取上一根快照（最近一次评估），lookback=N 取 N 次前。
        if (historicalSnapshots is not null && leaf.Lookback.HasValue)
        {
            var idx = historicalSnapshots.Count - leaf.Lookback.Value;
            if (idx >= 0 && idx < historicalSnapshots.Count
                && historicalSnapshots[idx].TryGetValue(leaf.Indicator, out var snapValue))
            {
                actual = snapValue;
                return true;
            }
        }

        // 历史不足时退化为当前值（保证规则在启动初期也能稳定评估，而非恒 false）
        return indicatorValues.TryGetValue(leaf.Indicator, out actual);
    }

    /// <summary>解析叶节点比较基准。带 ref 时乘数默认为 1；无 ref 时 value 必填。无法解析返回 false。</summary>
    private static bool TryResolveCompareValue(
        ConditionNode leaf, Dictionary<string, decimal> values, out decimal compared)
    {
        if (!string.IsNullOrEmpty(leaf.Ref))
        {
            if (values.TryGetValue(leaf.Ref, out var refVal))
            {
                compared = refVal * (leaf.Value ?? 1m);
                return true;
            }
            compared = 0m;
            return false;
        }

        if (leaf.Value is null)
        {
            compared = 0m;
            return false;
        }

        compared = leaf.Value.Value;
        return true;
    }

    private static RuleDecision ExecuteAction(
        TradingRule rule,
        Dictionary<string, decimal> indicators)
    {
        var action = rule.Then;
        var reason = ResolveTemplate(action.Reason ?? rule.Name, indicators);

        // size 统一解析为绝对 quote 金额：
        //   * fixed（默认）→ action.Size
        //   * multiplier   → action.Size × indicators[ref]（ref 缺失则退化为 fixed）
        var size = action.Size;
        if (action.SizeType == "multiplier" && action.SizeMultiplierRef is not null)
        {
            if (indicators.TryGetValue(action.SizeMultiplierRef, out var multiplier))
                size = action.Size * Math.Max(1, multiplier);
        }

        return action.Type switch
        {
            RuleActionType.Buy => new RuleDecision(RuleDecisionAction.Buy, size, action.SizeType, reason),
            RuleActionType.Sell => new RuleDecision(RuleDecisionAction.Sell, size, action.SizeType, reason),
            RuleActionType.SellAll => new RuleDecision(RuleDecisionAction.SellAll, 0m, null, reason),
            _ => new RuleDecision(RuleDecisionAction.Hold, 0m, null, reason)
        };
    }

    private static string ResolveTemplate(string template, Dictionary<string, decimal> indicators)
    {
        var result = template;
        foreach (var (key, value) in indicators)
        {
            var placeholder = $"{{{{{key}}}}}";
            if (result.Contains(placeholder))
                result = result.Replace(placeholder, value.ToString("F2"));
        }
        return result;
    }
}
