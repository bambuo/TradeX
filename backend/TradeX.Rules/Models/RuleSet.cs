namespace TradeX.Rules.Models;

/// <summary>规则集（一个策略对应一个 RuleSet）</summary>
public sealed record RuleSet(
    string Code,
    string Name,
    IReadOnlyList<TradingRule> Rules,
    Dictionary<string, decimal>? Params = null);
