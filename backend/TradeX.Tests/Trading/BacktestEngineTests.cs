using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class BacktestEngineTests
{
    private readonly BacktestEngine _engine;
    private readonly ConditionTreeEvaluator _treeEvaluator;

    public BacktestEngineTests()
    {
        var indicatorService = new IndicatorService();
        _treeEvaluator = new ConditionTreeEvaluator();
        var conditionEvaluator = new ConditionEvaluator(_treeEvaluator);
        _engine = new BacktestEngine(indicatorService, conditionEvaluator);
    }

    [Fact]
    public void Run_NoEntryCondition_ReturnsZeroTrades()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":100}""",
            ExitConditionJson = "{}",
            SymbolIds = "BTCUSDT",
            Timeframe = "1h"
        };

        var candles = GenerateCandles(100, 50000);
        var (result, trades) = _engine.Run(strategy, candles);

        Assert.Empty(trades);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void Run_EntryAlwaysTrue_ExitAlwaysTrue_ProducesTrades()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":100}""",
            SymbolIds = "BTCUSDT",
            Timeframe = "1h"
        };

        var candles = GenerateCandles(200, 50000);
        var (result, trades) = _engine.Run(strategy, candles);

        Assert.True(result.TotalTrades >= 1);
        Assert.NotEqual(0, result.TotalReturnPercent);
    }

    [Fact]
    public void Run_InsufficientData_ReturnsEmptyResult()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""",
            ExitConditionJson = "{}",
            SymbolIds = "BTCUSDT",
            Timeframe = "1h"
        };

        var candles = GenerateCandles(10, 50000);
        var (result, trades) = _engine.Run(strategy, candles);

        Assert.Equal(0, result.TotalTrades);
        Assert.Empty(trades);
    }

    [Fact]
    public void Run_ExitNeverTriggers_ClosesAtLastCandle()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":100}""",
            SymbolIds = "BTCUSDT",
            Timeframe = "1h"
        };

        var candles = GenerateCandles(200, 50000);
        var (result, trades) = _engine.Run(strategy, candles);

        // Only enters once, exits at end (auto-close)
        Assert.True(result.TotalTrades >= 1);
        var lastTrade = trades[^1];
        Assert.Equal(candles[^1].Timestamp, lastTrade.ExitTime);
    }

    [Fact]
    public void Run_CalculatesMetrics_Correctly()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""",
            ExitConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":70}""",
            SymbolIds = "BTCUSDT",
            Timeframe = "1h"
        };

        var candles = GenerateCandles(300, 50000);
        var (result, trades) = _engine.Run(strategy, candles);

        Assert.Equal(trades.Count, result.TotalTrades);
        Assert.InRange(result.TotalReturnPercent, -1000m, 1000m);
        Assert.InRange(result.MaxDrawdownPercent, 0m, 100m);
        Assert.InRange(result.WinRate, 0m, 100m);
    }

    private static List<Candle> GenerateCandles(int count, decimal basePrice)
    {
        var random = new Random(42);
        var candles = new List<Candle>(count);
        var price = basePrice;

        for (var i = 0; i < count; i++)
        {
            // Sine wave with random noise
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
