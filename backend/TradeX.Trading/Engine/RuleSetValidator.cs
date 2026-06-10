using System.Text.Json;
using TradeX.Indicators;

namespace TradeX.Trading.Engine;

/// <summary>
/// 校验统一规则集（RuleSet）JSON 格式的合法性：
///   * 顶层必需字段：code, rules
///   * 每条规则必需字段：code, name, when, then
///   * when 条件树复用 <see cref="ConditionTreeValidator"/> 的校验逻辑（含指标注册检查）
///   * then 动作中 action 必须为有效枚举值
///   * constraints 中各值的合理性检查
///   * context 必须为有效枚举值
///   * NOT 运算符必须包含恰好 1 个子条件
/// </summary>
public sealed class RuleSetValidator(ConditionTreeValidator conditionValidator)
{
    private static readonly HashSet<string> ValidActionTypes =
        new(StringComparer.OrdinalIgnoreCase) { "buy", "sell", "sellall", "hold" };

    private static readonly HashSet<string> ValidContexts =
        new(StringComparer.OrdinalIgnoreCase) { "any", "noposition", "hasposition" };

    // 仅保留纯函数、回测与实盘行为一致的尺寸模式：
    //   fixed       — 绝对 quote 金额
    //   multiplier  — Size × 引用指标（如 POSITION_COUNT，用于金字塔加仓）
    // percent（按账户权益百分比）依赖实盘逐 tick 拉取余额，破坏纯函数模型且会造成回测/实盘背离；
    // grid 无明确语义。两者均不在支持之列。
    private static readonly HashSet<string> ValidSizeTypes =
        new(StringComparer.OrdinalIgnoreCase) { "fixed", "multiplier" };

    public RuleSetValidationResult Validate(string ruleSetJson)
    {
        if (string.IsNullOrWhiteSpace(ruleSetJson))
            return RuleSetValidationResult.Failed([new("$", "规则集 JSON 为空")]);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(ruleSetJson); }
        catch (JsonException ex)
        {
            return RuleSetValidationResult.Failed([new("$", $"JSON 解析失败: {ex.Message}")]);
        }

        using (doc)
        {
            var issues = new List<ValidationIssue>();
            ValidateRoot(doc.RootElement, issues);
            return issues.Count == 0
                ? RuleSetValidationResult.Ok()
                : RuleSetValidationResult.Failed(issues);
        }
    }

    private void ValidateRoot(JsonElement root, List<ValidationIssue> issues)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new("$", $"必须是对象, 实际为 {root.ValueKind}"));
            return;
        }

        // code — 必需
        if (!root.TryGetProperty("code", out var codeEl) || codeEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(codeEl.GetString()))
            issues.Add(new("$.code", "必需的非空字符串字段"));

        // rules — 必需非空数组
        if (!root.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("$.rules", "必需的数组字段"));
            return;
        }

        var ruleCount = rulesEl.GetArrayLength();
        if (ruleCount == 0)
        {
            issues.Add(new("$.rules", "不能为空数组，至少需要一条规则"));
            return;
        }

        // params — 可选，值必须为数字
        if (root.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in paramsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Number)
                    issues.Add(new($"$.params.{prop.Name}", "参数值必须为数字"));
            }
        }

        for (var i = 0; i < ruleCount; i++)
            ValidateRule(rulesEl[i], $"$.rules[{i}]", issues);

        // 规则 code 必须在规则集内唯一——code 是触发追踪键与日志的标识，重复会导致
        // MinInterval 冷却互相串扰、日志归并混淆。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ruleCount; i++)
        {
            if (rulesEl[i].ValueKind == JsonValueKind.Object
                && rulesEl[i].TryGetProperty("code", out var c)
                && c.ValueKind == JsonValueKind.String
                && c.GetString() is { Length: > 0 } code
                && !seen.Add(code))
            {
                issues.Add(new($"$.rules[{i}].code", $"规则 code '{code}' 重复，须在规则集内唯一"));
            }
        }
    }

    private void ValidateRule(JsonElement rule, string path, List<ValidationIssue> issues)
    {
        if (rule.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(path, "必须是对象"));
            return;
        }

        // code — 必需
        if (!rule.TryGetProperty("code", out var codeEl) || codeEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(codeEl.GetString()))
            issues.Add(new($"{path}.code", "必需的非空字符串字段"));

        // name — 必需
        if (!rule.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameEl.GetString()))
            issues.Add(new($"{path}.name", "必需的非空字符串字段"));

        // when — 必需，复用条件树校验
        if (!rule.TryGetProperty("when", out var whenEl))
        {
            issues.Add(new($"{path}.when", "必需字段"));
        }
        else
        {
            // 注入到 ConditionTreeValidator 做指标校验（通过条件树的 Validate 来检查）
            var whenJson = whenEl.GetRawText();
            var condResult = conditionValidator.Validate(whenJson);
            if (!condResult.IsValid)
            {
                foreach (var ci in condResult.Issues)
                    issues.Add(ci with { Path = $"{path}.when.{ci.Path}" });
            }
        }

        // then — 必需
        if (!rule.TryGetProperty("then", out var thenEl))
        {
            issues.Add(new($"{path}.then", "必需字段"));
        }
        else
        {
            ValidateAction(thenEl, $"{path}.then", issues);
        }

        // context — 可选，必须为有效值
        if (rule.TryGetProperty("context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.String)
        {
            var ctx = ctxEl.GetString()!;
            if (!ValidContexts.Contains(ctx))
                issues.Add(new($"{path}.context", $"无效的上下文 '{ctx}', 允许: {string.Join("/", ValidContexts)}"));
        }

        // priority — 可选，必须为非负整数
        if (rule.TryGetProperty("priority", out var priEl) && priEl.ValueKind == JsonValueKind.Number)
        {
            if (!priEl.TryGetInt32(out var pri) || pri < 0)
                issues.Add(new($"{path}.priority", "必须为非负整数"));
        }

        // constraints — 可选
        if (rule.TryGetProperty("constraints", out var conEl))
            ValidateConstraints(conEl, $"{path}.constraints", issues);
    }

    private void ValidateAction(JsonElement element, string path, List<ValidationIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(path, "必须是对象"));
            return;
        }

        string? action = null;
        if (!element.TryGetProperty("action", out var actEl) || actEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(actEl.GetString()))
        {
            issues.Add(new($"{path}.action", "必需的字符串字段 (buy/sell/sellAll/hold)"));
        }
        else
        {
            action = actEl.GetString()!;
            if (!ValidActionTypes.Contains(action))
                issues.Add(new($"{path}.action", $"无效的动作类型 '{action}', 允许: buy/sell/sellAll/hold"));
        }

        // size 类字段（size / sizeType / sizeMultiplierRef）仅对 buy / sell 有意义。
        // sellAll（全平）与 hold（不动）会在运行时忽略它们，出现即视为配置错误。
        var sizeBearing = string.Equals(action, "buy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "sell", StringComparison.OrdinalIgnoreCase);
        if (action is not null && ValidActionTypes.Contains(action) && !sizeBearing)
        {
            foreach (var field in new[] { "size", "sizeType", "sizeMultiplierRef" })
                if (element.TryGetProperty(field, out _))
                    issues.Add(new($"{path}.{field}", $"动作 '{action}' 不接受 {field}（运行时忽略），仅 buy/sell 可用"));
            return;
        }

        // size — 可选，必须为正数
        if (element.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number)
        {
            if (sizeEl.TryGetDecimal(out var size) && size < 0)
                issues.Add(new($"{path}.size", "不能为负数"));
        }

        // sizeType — 可选
        var sizeType = element.TryGetProperty("sizeType", out var stEl) && stEl.ValueKind == JsonValueKind.String
            ? stEl.GetString()!
            : null;
        if (sizeType is not null && !ValidSizeTypes.Contains(sizeType))
            issues.Add(new($"{path}.sizeType", $"无效的 sizeType '{sizeType}', 允许: fixed/multiplier"));

        // sizeType=multiplier 必须提供 sizeMultiplierRef，且引用的指标须已知；
        // 否则运行时会静默退化为 fixed（倍率失效）。
        if (string.Equals(sizeType, "multiplier", StringComparison.OrdinalIgnoreCase))
        {
            var refName = element.TryGetProperty("sizeMultiplierRef", out var smrEl) && smrEl.ValueKind == JsonValueKind.String
                ? smrEl.GetString()
                : null;
            if (string.IsNullOrEmpty(refName))
                issues.Add(new($"{path}.sizeMultiplierRef", "sizeType=multiplier 时必须提供 sizeMultiplierRef"));
            else if (!conditionValidator.IsKnownIndicator(refName))
                issues.Add(new($"{path}.sizeMultiplierRef", $"倍率引用的指标 '{refName}' 未注册"));
        }

        // reason — 可选字符串，不校验具体内容
    }

    private static void ValidateConstraints(JsonElement element, string path, List<ValidationIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(path, "必须是对象"));
            return;
        }

        if (element.TryGetProperty("maxPositions", out var mpEl) && mpEl.ValueKind == JsonValueKind.Number)
        {
            if (!mpEl.TryGetInt32(out var mp) || mp <= 0)
                issues.Add(new($"{path}.maxPositions", "必须为正整数"));
        }

        if (element.TryGetProperty("maxPositionValue", out var mpvEl) && mpvEl.ValueKind == JsonValueKind.Number)
        {
            if (mpvEl.TryGetDecimal(out var mpv) && mpv <= 0)
                issues.Add(new($"{path}.maxPositionValue", "必须为正数"));
        }

        if (element.TryGetProperty("minInterval", out var miEl) && miEl.ValueKind == JsonValueKind.Number)
        {
            if (!miEl.TryGetInt32(out var mi) || mi <= 0)
                issues.Add(new($"{path}.minInterval", "必须为正整数（秒）"));
        }
    }
}

public sealed record RuleSetValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues)
{
    public static RuleSetValidationResult Ok() => new(true, []);
    public static RuleSetValidationResult Failed(IReadOnlyList<ValidationIssue> issues) => new(false, issues);
}
