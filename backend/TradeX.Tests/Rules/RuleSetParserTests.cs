using TradeX.Rules.Models;
using TradeX.Rules.Parsers;

namespace TradeX.Tests.Rules;

public class RuleSetParserTests
{
    [Fact]
    public void TryParse_FullRuleSet_Succeeds()
    {
        var json = """
            {
                "code": "ma_crossover",
                "name": "双均线趋势策略",
                "rules": [
                    {
                        "code": "entry",
                        "name": "金叉入场",
                        "when": {
                            "operator": "AND",
                            "conditions": [
                                {
                                    "indicator": "MA_FAST",
                                    "comparison": ">",
                                    "ref": "MA_SLOW",
                                    "value": 1.0
                                },
                                { "indicator": "VOLUME", "comparison": ">", "value": 1000 }
                            ]
                        },
                        "then": { "action": "buy", "size": 100, "sizeType": "fixed", "reason": "金叉触发入场" },
                        "context": "NoPosition",
                        "priority": 1,
                        "constraints": { "maxPositions": 1, "maxPositionValue": 50000 }
                    },
                    {
                        "code": "exit",
                        "name": "死叉出场",
                        "when": {
                            "operator": "AND",
                            "conditions": [
                                {
                                    "indicator": "MA_FAST",
                                    "comparison": "<",
                                    "ref": "MA_SLOW",
                                    "value": 1.0
                                }
                            ]
                        },
                        "then": { "action": "sellAll", "reason": "死叉触发出场" },
                        "context": "HasPosition"
                    }
                ],
                "params": {
                    "fastPeriod": 9,
                    "slowPeriod": 21
                }
            }
            """;

        var ruleSet = RuleSetParser.TryParse(json);

        Assert.NotNull(ruleSet);
        Assert.Equal("ma_crossover", ruleSet.Code);
        Assert.Equal("双均线趋势策略", ruleSet.Name);
        Assert.Equal(2, ruleSet.Rules.Count);
        Assert.NotNull(ruleSet.Params);
        Assert.Equal(9m, ruleSet.Params["fastPeriod"]);
        Assert.Equal(21m, ruleSet.Params["slowPeriod"]);

        // 入场规则
        var entry = ruleSet.Rules[0];
        Assert.Equal("entry", entry.Code);
        Assert.Equal("金叉入场", entry.Name);
        Assert.Equal(RuleContext.NoPosition, entry.Context);
        Assert.Equal(1, entry.Priority);
        Assert.NotNull(entry.Constraints);
        Assert.Equal(1, entry.Constraints.MaxPositions);
        Assert.Equal(50000m, entry.Constraints.MaxPositionValue);
        Assert.NotNull(entry.When);
        Assert.Equal("AND", entry.When.Operator);
        Assert.Equal(2, entry.When.Conditions.Count);
        Assert.Equal("MA_FAST", entry.When.Conditions[0].Indicator);
        Assert.Equal(">", entry.When.Conditions[0].Comparison);
        Assert.Equal("MA_SLOW", entry.When.Conditions[0].Ref);
        Assert.Equal(1.0m, entry.When.Conditions[0].Value);
        Assert.Equal(RuleActionType.Buy, entry.Then.Type);
        Assert.Equal(100m, entry.Then.Size);
        Assert.Equal("fixed", entry.Then.SizeType);

        // 出场规则
        var exit = ruleSet.Rules[1];
        Assert.Equal("exit", exit.Code);
        Assert.Equal(RuleContext.HasPosition, exit.Context);
        Assert.Equal(RuleActionType.SellAll, exit.Then.Type);
    }

    [Fact]
    public void TryParse_EmptyJson_ReturnsNull()
    {
        Assert.Null(RuleSetParser.TryParse(""));
        Assert.Null(RuleSetParser.TryParse("   "));
        Assert.Null(RuleSetParser.TryParse(null!));
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsNull()
    {
        Assert.Null(RuleSetParser.TryParse("{invalid}"));
    }

    [Fact]
    public void TryParse_MinimalRule_Succeeds()
    {
        var json = """
            {
                "code": "simple",
                "name": "简单策略",
                "rules": [
                    {
                        "code": "r1",
                        "name": "恒真买入",
                        "when": { "operator": "TRUE" },
                        "then": { "action": "buy" }
                    }
                ]
            }
            """;

        var ruleSet = RuleSetParser.TryParse(json);

        Assert.NotNull(ruleSet);
        Assert.Equal("simple", ruleSet.Code);
        Assert.Single(ruleSet.Rules);

        var rule = ruleSet.Rules[0];
        Assert.Equal("r1", rule.Code);
        Assert.Equal(RuleContext.Any, rule.Context);
        Assert.Equal(0, rule.Priority);
        Assert.Null(rule.Constraints);
        Assert.Equal(RuleActionType.Buy, rule.Then.Type);
        Assert.Equal(0m, rule.Then.Size);
        Assert.Null(rule.Then.SizeType);
        Assert.Null(rule.Then.Reason);
    }

    [Fact]
    public void TryParse_NestedConditions_Succeeds()
    {
        var json = """
            {
                "code": "nested",
                "name": "嵌套条件",
                "rules": [
                    {
                        "code": "r1",
                        "name": "复杂条件",
                        "when": {
                            "operator": "OR",
                            "conditions": [
                                {
                                    "operator": "AND",
                                    "conditions": [
                                        { "indicator": "RSI", "comparison": "<", "value": 30 },
                                        { "indicator": "CLOSE", "comparison": "<", "ref": "BB_LOWER", "value": 1.0 }
                                    ]
                                },
                                {
                                    "indicator": "CLOSE", "comparison": ">", "ref": "BB_UPPER", "value": 1.0
                                }
                            ]
                        },
                        "then": { "action": "buy" }
                    }
                ]
            }
            """;

        var ruleSet = RuleSetParser.TryParse(json);

        Assert.NotNull(ruleSet);
        var rule = ruleSet.Rules[0];
        Assert.NotNull(rule.When);
        Assert.Equal("OR", rule.When.Operator);
        Assert.Equal(2, rule.When.Conditions.Count);

        // AND 子节点
        var andNode = rule.When.Conditions[0];
        Assert.Equal("AND", andNode.Operator);
        Assert.Equal(2, andNode.Conditions.Count);
        Assert.Equal("RSI", andNode.Conditions[0].Indicator);
        Assert.Equal("<", andNode.Conditions[0].Comparison);
        Assert.Equal(30m, andNode.Conditions[0].Value);
        Assert.Equal("CLOSE", andNode.Conditions[1].Indicator);
        Assert.Equal("BB_LOWER", andNode.Conditions[1].Ref);

        // 叶子节点
        var leaf = rule.When.Conditions[1];
        Assert.Equal("CLOSE", leaf.Indicator);
        Assert.Equal(">", leaf.Comparison);
        Assert.Equal("BB_UPPER", leaf.Ref);
        Assert.Equal(1.0m, leaf.Value);
    }

    [Fact]
    public void TryParse_RuleActionWithSizeMultiplier_Succeeds()
    {
        var json = """
            {
                "code": "martingale",
                "name": "马丁格尔",
                "rules": [
                    {
                        "code": "add",
                        "name": "加仓",
                        "when": { "indicator": "DEVIATION_FROM_AVG", "comparison": "<", "value": -5 },
                        "then": {
                            "action": "buy",
                            "size": 200,
                            "sizeType": "multiplier",
                            "sizeMultiplierRef": "POSITION_COUNT",
                            "reason": "偏离 {{DEVIATION_FROM_AVG}}% 加仓"
                        },
                        "context": "HasPosition"
                    }
                ]
            }
            """;

        var ruleSet = RuleSetParser.TryParse(json);

        Assert.NotNull(ruleSet);
        var rule = ruleSet.Rules[0];
        Assert.Equal(RuleActionType.Buy, rule.Then.Type);
        Assert.Equal(200m, rule.Then.Size);
        Assert.Equal("multiplier", rule.Then.SizeType);
        Assert.Equal("POSITION_COUNT", rule.Then.SizeMultiplierRef);
        Assert.Contains("{{DEVIATION_FROM_AVG}}", rule.Then.Reason);
        Assert.Equal(RuleContext.HasPosition, rule.Context);
    }
}
