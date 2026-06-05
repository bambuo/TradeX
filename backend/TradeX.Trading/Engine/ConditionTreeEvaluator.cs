using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Engine;

public class ConditionTreeEvaluator : IConditionTreeEvaluator
{
    public bool Evaluate(ConditionNode node, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues)
    {
        return node.Operator switch
        {
            // AND 空数组按"空真"语义返回 true，与空 JSON 路径保持一致
            "AND" => node.Conditions.All(c => Evaluate(c, indicatorValues, previousValues)),
            "OR" => node.Conditions.Count > 0 && node.Conditions.Any(c => Evaluate(c, indicatorValues, previousValues)),
            "NOT" => node.Conditions.Count == 1 && !Evaluate(node.Conditions[0], indicatorValues, previousValues),
            _ => EvaluateLeaf(node, indicatorValues, previousValues)
        };
    }

    private static bool EvaluateLeaf(ConditionNode leaf, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues)
    {
        if (leaf.Indicator is null || leaf.Comparison is null || leaf.Value is null)
            return false;

        if (!indicatorValues.TryGetValue(leaf.Indicator, out var actual))
            return false;

        var hasRef = !string.IsNullOrEmpty(leaf.Ref);
        var compareValue = leaf.Value.Value;
        if (hasRef && indicatorValues.TryGetValue(leaf.Ref!, out var refVal))
            compareValue = refVal * leaf.Value.Value;

        var prevHasValue = previousValues.TryGetValue(leaf.Indicator, out var prev);

        // 穿越判定的 prev 端比较基准：指标对指标（带 Ref）时须用“上一根”的参照值，
        // 否则会拿上一根被测指标与当前参照指标比较，导致金叉/死叉在错误时机触发或漏触发。
        var prevCompareValue = compareValue;
        if (hasRef && previousValues.TryGetValue(leaf.Ref!, out var prevRefVal))
            prevCompareValue = prevRefVal * leaf.Value.Value;

        return leaf.Comparison switch
        {
            ">" => actual > compareValue,
            "<" => actual < compareValue,
            ">=" => actual >= compareValue,
            "<=" => actual <= compareValue,
            "==" => Math.Abs(actual - compareValue) < 0.0001m,
            "CA" => prevHasValue && prev <= prevCompareValue && actual > compareValue,
            "CB" => prevHasValue && prev >= prevCompareValue && actual < compareValue,
            _ => false
        };
    }
}

public class ConditionEvaluator(IConditionTreeEvaluator treeEvaluator) : IConditionEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool Evaluate(string conditionJson, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues)
    {
        // 空条件视为不触发，避免每根 K 线都开/平仓
        if (string.IsNullOrWhiteSpace(conditionJson) || conditionJson == "{}")
            return false;

        ConditionNode? node;
        try
        {
            node = JsonSerializer.Deserialize<ConditionNode>(conditionJson, JsonOptions);
        }
        catch (JsonException)
        {
            // 历史策略可能存有损坏 JSON，按"不触发"处理而非让整轮回测崩溃
            return false;
        }

        if (node is null)
            return false;

        return treeEvaluator.Evaluate(node, indicatorValues, previousValues);
    }
}
