using System.Text.Json;
using TradeX.Core.Models;

namespace TradeX.Core.ValueObjects;

/// <summary>
/// 条件树值对象。封装策略入场/出场条件的 JSON 序列化/反序列化与求值。
/// 不可变，空树视为"不触发"。
/// </summary>
public sealed class ConditionTree
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConditionNode? _root;

    /// <summary>空条件树（不触发）。</summary>
    public static readonly ConditionTree Empty = new((ConditionNode?)null);

    /// <summary>从 JSON 字符串解析。</summary>
    public static ConditionTree FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return Empty;

        ConditionNode? node;
        try
        {
            node = JsonSerializer.Deserialize<ConditionNode>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return Empty;
        }

        return new ConditionTree(node);
    }

    private ConditionTree(ConditionNode? root) => _root = root;

    /// <summary>当前条件树是否有实际条件。</summary>
    public bool HasConditions => _root is not null;

    /// <summary>求值。空树返回 false。</summary>
    public bool Evaluate(Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues)
    {
        if (_root is null) return false;
        return EvaluateNode(_root, indicatorValues, previousValues);
    }

    private static bool EvaluateNode(ConditionNode node, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues)
    {
        // 叶子节点：有 Indicator 和 Comparison 但无子条件
        if (node.Indicator is not null && node.Comparison is not null)
            return EvaluateLeaf(node, indicatorValues, previousValues);

        return node.Operator switch
        {
            "AND" => node.Conditions.All(c => EvaluateNode(c, indicatorValues, previousValues)),
            "OR" => node.Conditions.Count > 0 && node.Conditions.Any(c => EvaluateNode(c, indicatorValues, previousValues)),
            "NOT" => node.Conditions.Count == 1 && !EvaluateNode(node.Conditions[0], indicatorValues, previousValues),
            _ => false
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
            "CrossAbove" or "CA" => prevHasValue && prev <= prevCompareValue && actual > compareValue,
            "CrossBelow" or "CB" => prevHasValue && prev >= prevCompareValue && actual < compareValue,
            _ => false
        };
    }
}
