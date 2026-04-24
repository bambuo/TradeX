using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class ConditionTreeEvaluator : IConditionTreeEvaluator
{
    public bool Evaluate(ConditionNode node, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues)
    {
        return node.Operator switch
        {
            "AND" => node.Conditions.Count > 0 && node.Conditions.All(c => Evaluate(c, indicatorValues, previousValues)),
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

        return leaf.Comparison switch
        {
            ">" => actual > leaf.Value,
            "<" => actual < leaf.Value,
            ">=" => actual >= leaf.Value,
            "<=" => actual <= leaf.Value,
            "==" => Math.Abs(actual - leaf.Value.Value) < 0.0001m,
            "CrossAbove" => previousValues.TryGetValue(leaf.Indicator, out var prev)
                && prev <= leaf.Value && actual > leaf.Value,
            "CrossBelow" => previousValues.TryGetValue(leaf.Indicator, out var prev)
                && prev >= leaf.Value && actual < leaf.Value,
            _ => false
        };
    }
}

public class ConditionEvaluator(IConditionTreeEvaluator treeEvaluator) : IConditionEvaluator
{
    public bool Evaluate(string conditionJson, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues)
    {
        if (string.IsNullOrWhiteSpace(conditionJson) || conditionJson == "{}")
            return true;

        var node = JsonSerializer.Deserialize<ConditionNode>(conditionJson);
        if (node is null)
            return true;

        return treeEvaluator.Evaluate(node, indicatorValues, previousValues);
    }
}
