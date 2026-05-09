using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class BacktestEngineTests
{
    private readonly BacktestEngine _engine;

    public BacktestEngineTests()
    {
        _engine = new BacktestEngine();
    }

    [Fact]
    public void Run_NoEntryCondition_ReturnsZeroTrades()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":100}""",
            ExitCondition = "{}"
        };

        var candles = GenerateCandles(100, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", candles);

        Assert.Empty(trades);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void Run_EntryAlwaysTrue_ExitAlwaysTrue_ProducesTrades()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitCondition = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":100}"""
        };

        var candles = GenerateCandles(200, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", candles);

        Assert.True(result.TotalTrades >= 1);
        Assert.NotEqual(0, result.TotalReturnPercent);
    }

    [Fact]
    public void Run_InsufficientData_ReturnsEmptyResult()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""",
            ExitCondition = "{}"
        };

        var candles = GenerateCandles(10, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", candles);

        Assert.Equal(0, result.TotalTrades);
        Assert.Empty(trades);
    }

    [Fact]
    public void Run_ExitNeverTriggers_ClosesAtLastCandle()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitCondition = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":100}"""
        };

        var candles = GenerateCandles(200, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", candles);

        Assert.True(result.TotalTrades >= 1);
        var lastTrade = trades[^1];
        Assert.Equal(candles[^1].Timestamp, lastTrade.ExitedAt);
    }

    [Fact]
    public void Run_CalculatesMetrics_Correctly()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""",
            ExitCondition = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":70}"""
        };

        var candles = GenerateCandles(300, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", candles);

        Assert.Equal(trades.Count, result.TotalTrades);
        Assert.InRange(result.TotalReturnPercent, -1000m, 1000m);
        Assert.InRange(result.MaxDrawdownPercent, 0m, 100m);
        Assert.InRange(result.WinRate, 0m, 100m);
    }

    [Fact]
    public void Run_AnalysisJson_HasNonZeroIndicators()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitCondition = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":100}"""
        };

        var candles = GenerateCandles(300, 50000);
        var (_, _, analysis) = _engine.Run(strategy, "BTCUSDT", candles);

        Assert.NotEmpty(analysis);

        var last = analysis[^1];
        var keys = new[] { "SMA_20", "SMA_50", "EMA_20", "MACD_LINE", "MACD_SIGNAL", "BB_UPPER", "BB_LOWER" };
        foreach (var key in keys)
        {
            Assert.True(last.IndicatorValues.ContainsKey(key), $"缺少指标: {key}");
            Assert.NotEqual(0, last.IndicatorValues[key]);
        }
    }

    private static List<Candle> GenerateCandles(int count, decimal basePrice)
    {
        var random = new Random(42);
        var candles = new List<Candle>(count);
        var price = basePrice;

        for (var i = 0; i < count; i++)
        {
            var trend = (decimal)Math.Sin(i / 10.0) * 500;
            var noise = (decimal)(random.NextDouble() - 0.5) * 200;
            price += noise;

            var open = price;
            var close = price + trend * 0.1m + noise * 0.5m;
            var high = Math.Max(open, close) + Math.Abs(noise) * 0.5m;
            var low = Math.Min(open, close) - Math.Abs(noise) * 0.5m;
            price = close;

            candles.Add(new Candle(
                DateTime.UtcNow.AddHours(-(count - i)),
                open, high, low, close,
                (long)(random.NextDouble() * 1000 + 100)));
        }

        return candles;
    }
}
