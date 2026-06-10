namespace TradeX.Rules.Models;

/// <summary>规则评估决策</summary>
public sealed record RuleDecision(
    RuleDecisionAction Action,
    decimal Size = 0m,
    string? SizeType = null,
    string? Reason = null);

public enum RuleDecisionAction
{
    Buy,
    Sell,
    SellAll,
    Hold
}
