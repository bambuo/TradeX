using NSubstitute;
using TradeX.Indicators;

namespace TradeX.Tests.Trading;

public class RuleSetValidatorTests
{
    private static RuleSetValidator Build()
    {
        var reg = Substitute.For<IIndicatorRegistry>();
        reg.RegisteredNames.Returns(["RSI", "SMA_20", "SMA_50", "CLOSE"]);
        return new RuleSetValidator(new ConditionTreeValidator(reg));
    }

    [Fact]
    public void Validate_ValidFullRuleSet_ReturnsOk()
    {
        var json = """
            {
                "code": "ma_crossover",
                "name": "双均线策略",
                "rules": [
                    {
                        "code": "entry",
                        "name": "金叉入场",
                        "when": { "operator": "AND", "conditions": [
                            { "indicator": "SMA_20", "comparison": ">", "ref": "SMA_50", "value": 1.0 }
                        ]},
                        "then": { "action": "buy", "size": 100, "sizeType": "fixed", "reason": "金叉触发" },
                        "context": "NoPosition",
                        "priority": 1,
                        "constraints": { "maxPositions": 3, "maxPositionValue": 50000, "minInterval": 300 }
                    }
                ],
                "params": { "fastPeriod": 9 }
            }
            """;
        var result = Build().Validate(json);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyJson_Fails()
    {
        var result = Build().Validate("");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$" && i.Message.Contains("空"));
    }

    [Fact]
    public void Validate_MalformedJson_Fails()
    {
        var result = Build().Validate("{invalid}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("JSON"));
    }

    [Fact]
    public void Validate_MissingCode_Fails()
    {
        var json = """{"name":"test","rules":[{"code":"r1","name":"恒真","when":{"operator":"TRUE"},"then":{"action":"buy"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.code");
    }

    [Fact]
    public void Validate_MissingRules_Fails()
    {
        var json = """{"code":"test","name":"test"}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.rules");
    }

    [Fact]
    public void Validate_EmptyRulesArray_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.rules" && i.Message.Contains("空数组"));
    }

    [Fact]
    public void Validate_RuleMissingCode_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"name":"no code","when":{"operator":"TRUE"},"then":{"action":"buy"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.EndsWith(".code"));
    }

    [Fact]
    public void Validate_RuleMissingWhen_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"no when","then":{"action":"buy"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.EndsWith(".when"));
    }

    [Fact]
    public void Validate_RuleMissingThen_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"no then","when":{"operator":"TRUE"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.EndsWith(".then"));
    }

    [Fact]
    public void Validate_InvalidActionType_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"bad action","when":{"operator":"TRUE"},"then":{"action":"invalid_action"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("无效的动作类型"));
    }

    [Fact]
    public void Validate_InvalidContext_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"bad ctx","when":{"operator":"TRUE"},"then":{"action":"buy"},"context":"InvalidContext"}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("无效的上下文"));
    }

    [Fact]
    public void Validate_NegativeMinInterval_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"bad interval","when":{"operator":"TRUE"},"then":{"action":"buy"},"constraints":{"minInterval":-1}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.Contains("minInterval"));
    }

    [Fact]
    public void Validate_ZeroMinInterval_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"zero interval","when":{"operator":"TRUE"},"then":{"action":"buy"},"constraints":{"minInterval":0}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.Contains("minInterval"));
    }

    [Fact]
    public void Validate_ValidMinInterval_Ok()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"good interval","when":{"operator":"TRUE"},"then":{"action":"buy"},"constraints":{"minInterval":60}}]}""";
        var result = Build().Validate(json);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NegativeMaxPositions_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"bad max pos","when":{"operator":"TRUE"},"then":{"action":"buy"},"constraints":{"maxPositions":-1}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.Contains("maxPositions"));
    }

    [Fact]
    public void Validate_NegativeSize_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"neg size","when":{"operator":"TRUE"},"then":{"action":"buy","size":-100}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("不能为负数"));
    }

    [Fact]
    public void Validate_ValidActionWithSizeType_Ok()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"pyramid","when":{"operator":"TRUE"},"then":{"action":"buy","size":100,"sizeType":"multiplier","sizeMultiplierRef":"POSITION_COUNT"}}]}""";
        var result = Build().Validate(json);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_GridSizeType_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"grid","when":{"operator":"TRUE"},"then":{"action":"buy","size":100,"sizeType":"grid"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("sizeType"));
    }

    [Fact]
    public void Validate_ContextIndicatorInWhen_Ok()
    {
        // 上下文指标（DEVIATION_FROM_AVG 等）虽不在技术指标注册表，但规则引用它们合法
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"加仓","when":{"indicator":"DEVIATION_FROM_AVG","comparison":"<","value":-5},"then":{"action":"buy"},"context":"hasPosition"}]}""";
        var result = Build().Validate(json);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RelativeRefWithoutValue_Ok()
    {
        // 带 ref 的相对比较，value 可省略（乘数默认 1）
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"金叉","when":{"indicator":"SMA_20","comparison":"CA","ref":"SMA_50"},"then":{"action":"buy"}}]}""";
        var result = Build().Validate(json);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateRuleCode_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"dup","name":"a","when":{"operator":"TRUE"},"then":{"action":"buy"}},{"code":"dup","name":"b","when":{"operator":"TRUE"},"then":{"action":"sellAll"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("重复"));
    }

    [Fact]
    public void Validate_MultiplierWithoutRef_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"加仓","when":{"operator":"TRUE"},"then":{"action":"buy","size":100,"sizeType":"multiplier"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.Contains("sizeMultiplierRef"));
    }

    [Fact]
    public void Validate_MultiplierWithUnknownRef_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"加仓","when":{"operator":"TRUE"},"then":{"action":"buy","size":100,"sizeType":"multiplier","sizeMultiplierRef":"GHOST"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.Contains("sizeMultiplierRef") && i.Message.Contains("未注册"));
    }

    [Fact]
    public void Validate_SizeOnSellAll_Fails()
    {
        // sellAll 全平，size 类字段运行时被忽略，出现即报错
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"出场","context":"hasPosition","when":{"operator":"TRUE"},"then":{"action":"sellAll","size":100}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.EndsWith(".size") && i.Message.Contains("不接受"));
    }

    [Fact]
    public void Validate_SizeTypeOnHold_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"观望","when":{"operator":"TRUE"},"then":{"action":"hold","sizeType":"fixed"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.EndsWith(".sizeType") && i.Message.Contains("不接受"));
    }

    [Fact]
    public void Validate_SellWithSize_Ok()
    {
        // sell 支持按金额减仓，size 合法
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"减仓","context":"hasPosition","when":{"operator":"TRUE"},"then":{"action":"sell","size":50}}]}""";
        var result = Build().Validate(json);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InvalidSizeType_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"bad sizeType","when":{"operator":"TRUE"},"then":{"action":"buy","sizeType":"invalid"}}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("sizeType"));
    }

    [Fact]
    public void Validate_NegativePriority_Fails()
    {
        var json = """{"code":"test","name":"test","rules":[{"code":"r1","name":"neg pri","when":{"operator":"TRUE"},"then":{"action":"buy"},"priority":-1}]}""";
        var result = Build().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path.Contains("priority"));
    }

    [Fact]
    public void Validate_ValidMinimalRuleSet_Ok()
    {
        var json = """{"code":"simple","name":"简单","rules":[{"code":"r1","name":"恒真买入","when":{"operator":"TRUE"},"then":{"action":"buy"}}]}""";
        var result = Build().Validate(json);
        Assert.True(result.IsValid);
    }
}
