using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeX.Core.Models;
using TradeX.Rules.Models;

namespace TradeX.Rules.Parsers;

public static class RuleSetParser
{
    public static RuleSet? TryParse(string json, ILogger? logger = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var code = root.TryGetProperty("code", out var codeElem)
                ? codeElem.GetString() ?? "unknown"
                : "unknown";
            var name = root.TryGetProperty("name", out var nameElem)
                ? nameElem.GetString() ?? code
                : code;

            if (!root.TryGetProperty("rules", out var rulesElement) || rulesElement.ValueKind != JsonValueKind.Array)
                return null;

            var rules = new List<TradingRule>();

            foreach (var ruleElement in rulesElement.EnumerateArray())
            {
                var rule = ParseRule(ruleElement, logger);
                // fail-closed：任一规则解析失败即拒绝整个规则集，
                // 避免"半套规则生效"（如出场规则坏掉只剩入场 → 只买不卖）。
                if (rule is null)
                {
                    logger?.LogWarning("规则集 {Code} 含无法解析的规则，整集拒绝", code);
                    return null;
                }
                rules.Add(rule);
            }

            if (rules.Count == 0)
                return null;

            JsonElement? paramsElement = root.TryGetProperty("params", out var p) ? p : null;
            Dictionary<string, decimal>? paramDict = null;

            if (paramsElement is not null && paramsElement.Value.ValueKind == JsonValueKind.Object)
            {
                paramDict = [];
                foreach (var prop in paramsElement.Value.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        paramDict[prop.Name] = prop.Value.GetDecimal();
                }
            }

            return new RuleSet(code, name, rules, paramDict);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "解析规则集 JSON 失败");
            return null;
        }
    }

    private static TradingRule? ParseRule(JsonElement element, ILogger? logger = null)
    {
        try
        {
            var code = element.GetProperty("code").GetString() ?? Guid.NewGuid().ToString();
            var name = element.GetProperty("name").GetString() ?? code;

            var whenElement = element.GetProperty("when");
            var when = ParseConditionNode(whenElement);

            var thenElement = element.GetProperty("then");
            var then = ParseAction(thenElement);

            var context = element.TryGetProperty("context", out var ctxElem)
                ? Enum.Parse<RuleContext>(ctxElem.GetString() ?? "Any", true)
                : RuleContext.Any;

            var priority = element.TryGetProperty("priority", out var priElem)
                ? priElem.GetInt32()
                : 0;

            RuleConstraints? constraints = null;
            if (element.TryGetProperty("constraints", out var conElem))
                constraints = ParseConstraints(conElem);

            return new TradingRule(code, name, when, then, context, priority, constraints);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "解析单条规则失败");
            return null;
        }
    }

    private static ConditionNode? ParseConditionNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var op = element.TryGetProperty("operator", out var opElem)
            ? opElem.GetString()
            : null;

        var node = new ConditionNode
        {
            Operator = op ?? "AND",
            Indicator = element.TryGetProperty("indicator", out var indElem) ? indElem.GetString() : null,
            Comparison = element.TryGetProperty("comparison", out var cmpElem) ? cmpElem.GetString() : null,
            Ref = element.TryGetProperty("ref", out var refElem) ? refElem.GetString() : null
        };

        if (element.TryGetProperty("value", out var valElem))
        {
            if (valElem.ValueKind == JsonValueKind.Number)
                node.Value = valElem.GetDecimal();
        }

        if (element.TryGetProperty("lookback", out var lbElem) && lbElem.ValueKind == JsonValueKind.Number)
        {
            if (lbElem.TryGetInt32(out var lb) && lb > 0)
                node.Lookback = lb;
        }

        // 解析子条件
        if (element.TryGetProperty("conditions", out var condsElem) && condsElem.ValueKind == JsonValueKind.Array)
        {
            var conditions = new List<ConditionNode>();
            foreach (var child in condsElem.EnumerateArray())
            {
                var childNode = ParseConditionNode(child);
                if (childNode is not null)
                    conditions.Add(childNode);
            }
            node.Conditions = conditions;
        }

        return node;
    }

    private static RuleAction ParseAction(JsonElement element)
    {
        var typeStr = element.GetProperty("action").GetString() ?? "Hold";
        var type = Enum.Parse<RuleActionType>(typeStr, true);

        var size = element.TryGetProperty("size", out var sizeElem) && sizeElem.ValueKind == JsonValueKind.Number
            ? sizeElem.GetDecimal()
            : 0m;

        var sizeType = element.TryGetProperty("sizeType", out var stElem)
            ? stElem.GetString()
            : null;

        var reason = element.TryGetProperty("reason", out var rElem)
            ? rElem.GetString()
            : null;

        var sizeMultiplierRef = element.TryGetProperty("sizeMultiplierRef", out var smrElem)
            ? smrElem.GetString()
            : null;

        return new RuleAction(type, size, sizeType, sizeMultiplierRef, reason);
    }

    private static RuleConstraints ParseConstraints(JsonElement element)
    {
        int? maxPositions = element.TryGetProperty("maxPositions", out var mpElem) && mpElem.ValueKind == JsonValueKind.Number
            ? mpElem.GetInt32()
            : null;

        decimal? maxPositionValue = element.TryGetProperty("maxPositionValue", out var mpvElem) && mpvElem.ValueKind == JsonValueKind.Number
            ? mpvElem.GetDecimal()
            : null;

        int? minInterval = element.TryGetProperty("minInterval", out var miElem) && miElem.ValueKind == JsonValueKind.Number
            ? miElem.GetInt32()
            : null;

        return new RuleConstraints(maxPositions, maxPositionValue, minInterval);
    }
}
