using TradeX.Indicators;

namespace TradeX.Tests.Trading;

public class IndicatorRegistryTests
{
    private static KlineWindow SampleWindow() => new(
        Prices: [1m, 2m, 3m, 4m, 5m],
        Volumes: [10L, 20L, 30L, 40L, 50L],
        Open: 4m, High: 5m, Low: 3m, Close: 5m);

    [Fact]
    public void Register_NewIndicator_AppearsInRegisteredNames()
    {
        var reg = new IndicatorRegistry();
        reg.Register("MY_IND", _ => 42m);

        Assert.Contains("MY_IND", reg.RegisteredNames);
    }

    [Fact]
    public void ComputeAll_RunsEveryRegisteredIndicator()
    {
        var reg = new IndicatorRegistry();
        reg.Register("A", _ => 1m);
        reg.Register("B", _ => 2m);

        var values = reg.ComputeAll(SampleWindow());

        Assert.Equal(1m, values["A"]);
        Assert.Equal(2m, values["B"]);
    }

    [Fact]
    public void Compute_SubsetByName_SkipsUnknown()
    {
        var reg = new IndicatorRegistry();
        reg.Register("A", _ => 1m);

        var values = reg.Compute(["A", "Z_UNKNOWN"], SampleWindow());

        Assert.Single(values);
        Assert.Equal(1m, values["A"]);
    }

    [Fact]
    public void Register_SameName_OverwritesPrevious()
    {
        var reg = new IndicatorRegistry();
        reg.Register("X", _ => 1m);
        reg.Register("X", _ => 99m);

        Assert.Equal(99m, reg.ComputeAll(SampleWindow())["X"]);
    }

    [Fact]
    public void RegisterDefaults_RegistersAllLegacyIndicators()
    {
        var reg = new IndicatorRegistry();
        DependencyInjection.RegisterDefaults(reg, new IndicatorService());

        // 锁定原 BacktestEngine 硬编码的 11 个指标全部就位
        var expected = new[] { "RSI", "SMA_20", "SMA_50", "EMA_20", "MACD_LINE", "MACD_SIGNAL",
            "BB_UPPER", "BB_LOWER", "OBV", "VOLUME_SMA", "RANGE_PCT" };
        foreach (var name in expected)
            Assert.Contains(name, reg.RegisteredNames);
    }

    [Fact]
    public void ExtendingRegistry_AfterDefaults_AddsCustomIndicator()
    {
        // 验收: 新增一个 KDJ_K 指标无需修改 BacktestEngine, 只需注册函数.
        var reg = new IndicatorRegistry();
        DependencyInjection.RegisterDefaults(reg, new IndicatorService());
        reg.Register("KDJ_K", w => w.Close);

        var values = reg.ComputeAll(SampleWindow());

        Assert.Equal(5m, values["KDJ_K"]);
        Assert.Equal(12, values.Count);  // 11 默认 + 1 自定义
    }
}
