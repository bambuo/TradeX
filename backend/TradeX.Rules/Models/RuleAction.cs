namespace TradeX.Rules.Models;

/// <summary>规则动作类型</summary>
public enum RuleActionType
{
    Buy,
    Sell,
    SellAll,
    Hold
}

/// <summary>规则动作</summary>
public sealed record RuleAction(
    RuleActionType Type,
    decimal Size = 0m,
    string? SizeType = null,          // fixed（默认，绝对 quote 金额）/ multiplier（Size × ref 指标）
    string? SizeMultiplierRef = null,  // 倍率参考指标名（如 POSITION_COUNT）
    string? Reason = null);
