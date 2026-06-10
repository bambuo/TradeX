using System.Text.Json;
using TradeX.Indicators;
using TradeX.Rules.Indicators;

namespace TradeX.Trading.Engine;

public sealed record ValidationIssue(string Path, string Message);

public sealed record ConditionTreeValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues)
{
    public static ConditionTreeValidationResult Ok() => new(true, []);
    public static ConditionTreeValidationResult Failed(IReadOnlyList<ValidationIssue> issues) => new(false, issues);
}

/// <summary>
/// 校验策略 EntryCondition / ExitCondition 的 JSON 条件树:
///   * 顶层及子节点的 Operator 限于 AND / OR / NOT / 空(叶节点)
///   * 叶节点必须有 Indicator + Comparison + Value
///   * Comparison 限于白名单 (含历史短代号 CA/CB)
///   * Ref 字段若存在, 引用的指标必须已注册
///   * NOT 必须正好 1 个子节点
/// </summary>
public sealed class ConditionTreeValidator(IIndicatorRegistry registry)
{
    private static readonly HashSet<string> ValidComparisons =
        new(StringComparer.Ordinal) { ">", "<", ">=", "<=", "==", "CA", "CB" };

    private static readonly HashSet<string> ValidGroupOperators =
        new(StringComparer.Ordinal) { "AND", "OR", "NOT", "TRUE" };

    public ConditionTreeValidationResult Validate(string conditionJson)
    {
        // 空 JSON 是合法的"不触发"占位, 不算错误
        if (string.IsNullOrWhiteSpace(conditionJson) || conditionJson == "{}")
            return ConditionTreeValidationResult.Ok();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(conditionJson); }
        catch (JsonException ex) { return ConditionTreeValidationResult.Failed([new("$", $"JSON 解析失败: {ex.Message}")]); }

        var issues = new List<ValidationIssue>();
        using (doc) ValidateNode(doc.RootElement, "$", issues);
        return issues.Count == 0 ? ConditionTreeValidationResult.Ok() : ConditionTreeValidationResult.Failed(issues);
    }

    private void ValidateNode(JsonElement node, string path, List<ValidationIssue> issues)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(path, $"必须是对象, 实际为 {node.ValueKind}"));
            return;
        }

        var op = node.TryGetProperty("operator", out var opEl) && opEl.ValueKind == JsonValueKind.String
            ? opEl.GetString() ?? string.Empty : string.Empty;

        // TRUE 恒真运算符：无需进一步校验
        if (op == "TRUE")
            return;

        if (ValidGroupOperators.Contains(op))
        {
            ValidateGroup(node, op, path, issues);
            return;
        }

        if (op.Length > 0)
        {
            issues.Add(new($"{path}.operator", $"不支持的运算符 '{op}', 允许: {string.Join("/", ValidGroupOperators)} 或留空表示叶节点"));
            return;
        }

        ValidateLeaf(node, path, issues);
    }

    private void ValidateGroup(JsonElement node, string op, string path, List<ValidationIssue> issues)
    {
        if (!node.TryGetProperty("conditions", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new($"{path}.conditions", $"{op} 节点必须包含 conditions 数组"));
            return;
        }

        var count = children.GetArrayLength();
        if (op == "NOT" && count != 1)
            issues.Add(new($"{path}.conditions", $"NOT 必须恰好包含 1 个子节点, 实际 {count}"));

        var idx = 0;
        foreach (var child in children.EnumerateArray())
        {
            ValidateNode(child, $"{path}.conditions[{idx++}]", issues);
        }
    }

    private void ValidateLeaf(JsonElement node, string path, List<ValidationIssue> issues)
    {
        if (!node.TryGetProperty("indicator", out var indEl) || indEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(indEl.GetString()))
            issues.Add(new($"{path}.indicator", "叶节点必须指定 indicator"));
        else
        {
            var indName = indEl.GetString()!;
            if (!IsKnownIndicator(indName))
                issues.Add(new($"{path}.indicator", $"指标 '{indName}' 未注册"));
        }

        if (!node.TryGetProperty("comparison", out var cmpEl) || cmpEl.ValueKind != JsonValueKind.String)
            issues.Add(new($"{path}.comparison", "叶节点必须指定 comparison"));
        else
        {
            var cmp = cmpEl.GetString() ?? string.Empty;
            if (!ValidComparisons.Contains(cmp))
                issues.Add(new($"{path}.comparison", $"不支持的比较运算符 '{cmp}', 允许: {string.Join("/", ValidComparisons)}"));
        }

        // lookback（可选）必须为正整数
        var hasLookback = node.TryGetProperty("lookback", out var lbEl);
        if (hasLookback && lbEl.ValueKind == JsonValueKind.Number)
        {
            if (!lbEl.TryGetInt32(out var lb) || lb <= 0)
                issues.Add(new($"{path}.lookback", "lookback 必须为正整数"));
        }
        else if (hasLookback)
        {
            issues.Add(new($"{path}.lookback", "lookback 必须为数字"));
        }

        // ref（相对比较）存在时引用的指标必须已知；此时 value 作为乘数可省略（默认 1）。
        var hasRef = node.TryGetProperty("ref", out var refEl)
            && refEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(refEl.GetString());
        if (hasRef && !IsKnownIndicator(refEl.GetString()!))
            issues.Add(new($"{path}.ref", $"相对比较引用的指标 '{refEl.GetString()}' 未注册"));

        var hasValue = node.TryGetProperty("value", out var valEl);
        if (hasValue && valEl.ValueKind == JsonValueKind.Null)
            issues.Add(new($"{path}.value", "value 不能为 null"));
        else if (hasValue && valEl.ValueKind != JsonValueKind.Number)
            issues.Add(new($"{path}.value", "value 必须为数字"));
        else if (!hasValue && !hasRef)
            issues.Add(new($"{path}.value", "叶节点必须指定 value (数字)，或通过 ref 做相对比较"));
    }

    /// <summary>技术指标（注册表）或上下文指标（由持仓状态派生）均视为合法引用。</summary>
    public bool IsKnownIndicator(string name)
        => registry.RegisteredNames.Contains(name) || ContextIndicators.All.Contains(name);
}
