using NSubstitute;
using TradeX.Indicators;
using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

public class ConditionTreeValidatorTests
{
    private static ConditionTreeValidator Build(params string[] registered)
    {
        var reg = Substitute.For<IIndicatorRegistry>();
        reg.RegisteredNames.Returns(registered);
        return new ConditionTreeValidator(reg);
    }

    [Fact]
    public void Validate_EmptyJson_ReturnsOk()
    {
        var r = Build("RSI").Validate("{}");
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Validate_ValidLeaf_ReturnsOk()
    {
        var r = Build("RSI").Validate("""{"Indicator":"RSI","Comparison":">","Value":30}""");
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Validate_UnknownIndicator_Fails()
    {
        var r = Build("RSI").Validate("""{"Indicator":"NOT_REAL","Comparison":">","Value":30}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("Indicator") && i.Message.Contains("未注册"));
    }

    [Fact]
    public void Validate_BadComparison_Fails()
    {
        var r = Build("RSI").Validate("""{"Indicator":"RSI","Comparison":"!!=","Value":30}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("Comparison"));
    }

    [Fact]
    public void Validate_NullValue_Fails()
    {
        var r = Build("RSI").Validate("""{"Indicator":"RSI","Comparison":">","Value":null}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("Value"));
    }

    [Fact]
    public void Validate_RefIndicatorNotRegistered_Fails()
    {
        var r = Build("SMA_50").Validate("""{"Indicator":"SMA_50","Comparison":">","Value":1.02,"Ref":"GHOST_IND"}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("Ref") && i.Message.Contains("GHOST_IND"));
    }

    [Fact]
    public void Validate_RefIndicatorRegistered_Ok()
    {
        var r = Build("SMA_50", "SMA_20").Validate("""{"Indicator":"SMA_50","Comparison":">","Value":1.02,"Ref":"SMA_20"}""");
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Validate_NotOperatorWithMultipleChildren_Fails()
    {
        var json = """{"Operator":"NOT","Conditions":[{"Indicator":"RSI","Comparison":">","Value":30},{"Indicator":"RSI","Comparison":">","Value":40}]}""";
        var r = Build("RSI").Validate(json);
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Message.Contains("NOT") && i.Message.Contains("1 个"));
    }

    [Fact]
    public void Validate_AndOperatorNestedLegacyCA_OK()
    {
        // 老短代号 CA/CB 仍是合法 (有向后兼容映射), validator 不阻塞它们
        var json = """{"Operator":"AND","Conditions":[{"Indicator":"SMA_20","Comparison":"CA","Value":50000}]}""";
        var r = Build("SMA_20").Validate(json);
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Validate_MalformedJson_Fails()
    {
        var r = Build("RSI").Validate("not json{");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Message.Contains("JSON"));
    }

    [Fact]
    public void Validate_UnknownOperator_Fails()
    {
        var r = Build("RSI").Validate("""{"Operator":"XOR","Conditions":[]}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("Operator"));
    }
}
