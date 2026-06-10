using TradeX.Core.Models;

namespace TradeX.Rules.Models;

/// <summary>交易规则：条件 → 动作的映射</summary>
public sealed record TradingRule(
    string Code,
    string Name,
    ConditionNode? When,              // null 表示恒真（如首次入场规则）
    RuleAction Then,
    RuleContext Context = RuleContext.Any,
    int Priority = 0,
    RuleConstraints? Constraints = null);
