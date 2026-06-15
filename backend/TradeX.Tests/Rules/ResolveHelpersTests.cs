using TradeX.Core.Rules;
using TradeX.Trading.Rules;

namespace TradeX.Tests.Rules;

/// <summary>ResolveHelpers 解析辅助方法测试（对应 Go resolve_test.go）。</summary>
public class ResolveHelpersTests
{
    private static ChainState NewState(
        Dictionary<string, decimal>? derived = null,
        Dictionary<string, Signal>? signals = null) => new()
    {
        DerivedValues = derived ?? [],
        Signals = signals ?? [],
    };

    // ── Resolve 基础解析 ──────────────────────────────────────

    [Fact]
    public void Resolve_DerivedTakesPrecedence()
    {
        var st = NewState(
            derived: new() { ["x"] = 5m },
            signals: new() { ["x"] = new Signal { Name = "x", Value = 9m } });

        var (value, found) = st.Resolve("x");
        Assert.True(found);
        Assert.Equal(5m, value);
    }

    [Fact]
    public void Resolve_FallsBackToSignal()
    {
        var st = NewState(
            derived: [],
            signals: new() { ["RSI_14"] = new Signal { Name = "RSI_14", Value = 28m } });

        var (value, found) = st.Resolve("RSI_14");
        Assert.True(found);
        Assert.Equal(28m, value);
    }

    [Fact]
    public void Resolve_NotFound()
    {
        var st = NewState();
        var (_, found) = st.Resolve("missing");
        Assert.False(found);
    }

    // ── ResolveRef 带来源标识 ─────────────────────────────────

    [Fact]
    public void ResolveRef_DerivedSource()
    {
        var st = NewState(
            derived: new() { ["d"] = 1m },
            signals: new() { ["s"] = new Signal { Name = "s", Value = 2m } });

        var (value, source, found) = st.ResolveRef("d");
        Assert.True(found);
        Assert.Equal(1m, value);
        Assert.Equal("derived", source);
    }

    [Fact]
    public void ResolveRef_SignalSource()
    {
        var st = NewState(
            derived: [],
            signals: new() { ["s"] = new Signal { Name = "s", Value = 2m } });

        var (value, source, found) = st.ResolveRef("s");
        Assert.True(found);
        Assert.Equal(2m, value);
        Assert.Equal("signal", source);
    }

    [Fact]
    public void ResolveRef_NotFound()
    {
        var st = NewState();
        var (_, source, found) = st.ResolveRef("missing");
        Assert.False(found);
        Assert.Equal("", source);
    }

    [Fact]
    public void ResolveRef_DerivedPriorityOverSignal()
    {
        var st = NewState(
            derived: new() { ["x"] = 10m },
            signals: new() { ["x"] = new Signal { Name = "x", Value = 20m } });

        var (value, source, found) = st.ResolveRef("x");
        Assert.True(found);
        Assert.Equal(10m, value);
        Assert.Equal("derived", source);
    }
}
