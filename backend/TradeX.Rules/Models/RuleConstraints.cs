namespace TradeX.Rules.Models;

/// <summary>规则约束（执行前检查）</summary>
public sealed record RuleConstraints(
    int? MaxPositions = null,
    decimal? MaxPositionValue = null,
    int? MinInterval = null);
