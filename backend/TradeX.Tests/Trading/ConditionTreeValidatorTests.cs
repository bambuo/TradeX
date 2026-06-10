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
        var r = Build("RSI").Validate("""{"indicator":"RSI","comparison":">","value":30}""");
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Validate_UnknownIndicator_Fails()
    {
        var r = Build("RSI").Validate("""{"indicator":"NOT_REAL","comparison":">","value":30}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("indicator") && i.Message.Contains("未注册"));
    }

    [Fact]
    public void Validate_BadComparison_Fails()
    {
        var r = Build("RSI").Validate("""{"indicator":"RSI","comparison":"!!=","value":30}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("comparison"));
    }

    [Fact]
    public void Validate_NullValue_Fails()
    {
        var r = Build("RSI").Validate("""{"indicator":"RSI","comparison":">","value":null}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("value"));
    }

    [Fact]
    public void Validate_RefIndicatorNotRegistered_Fails()
    {
        var r = Build("SMA_50").Validate("""{"indicator":"SMA_50","comparison":">","value":1.02,"ref":"GHOST_IND"}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("ref") && i.Message.Contains("GHOST_IND"));
    }

    [Fact]
    public void Validate_RefIndicatorRegistered_Ok()
    {
        var r = Build("SMA_50", "SMA_20").Validate("""{"indicator":"SMA_50","comparison":">","value":1.02,"ref":"SMA_20"}""");
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Validate_NotOperatorWithMultipleChildren_Fails()
    {
        var json = """{"operator":"NOT","conditions":[{"indicator":"RSI","comparison":">","value":30},{"indicator":"RSI","comparison":">","value":40}]}""";
        var r = Build("RSI").Validate(json);
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Message.Contains("NOT") && i.Message.Contains("1 个"));
    }

    [Fact]
    public void Validate_AndOperatorNestedLegacyCA_OK()
    {
        // 老短代号 CA/CB 仍是合法 (有向后兼容映射), validator 不阻塞它们
        var json = """{"operator":"AND","conditions":[{"indicator":"SMA_20","comparison":"CA","value":50000}]}""";
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
        var r = Build("RSI").Validate("""{"operator":"XOR","conditions":[]}""");
        Assert.False(r.IsValid);
        Assert.Contains(r.Issues, i => i.Path.Contains("operator"));
    }

    [Fact]
    public void Validate_TrueOperator_ReturnsOk()
    {
        var r = Build("RSI").Validate("""{"operator":"TRUE"}""");
        Assert.True(r.IsValid);
    }
}
