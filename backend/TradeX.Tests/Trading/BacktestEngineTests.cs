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
            ExitConditionJson = "{}"
        };

        var candles = GenerateCandles(100, 50000);
        var (result, trades, _) = _engine.Run(strategy, candles);

        Assert.Empty(trades);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void Run_EntryAlwaysTrue_ExitAlwaysTrue_ProducesTrades()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":100}"""
        };

        var candles = GenerateCandles(200, 50000);
        var (result, trades, _) = _engine.Run(strategy, candles);

        Assert.True(result.TotalTrades >= 1);
        Assert.NotEqual(0, result.TotalReturnPercent);
    }

    [Fact]
    public void Run_InsufficientData_ReturnsEmptyResult()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""",
            ExitConditionJson = "{}"
        };

        var candles = GenerateCandles(10, 50000);
        var (result, trades, _) = _engine.Run(strategy, candles);

        Assert.Equal(0, result.TotalTrades);
        Assert.Empty(trades);
    }

    [Fact]
    public void Run_ExitNeverTriggers_ClosesAtLastCandle()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":100}"""
        };

        var candles = GenerateCandles(200, 50000);
        var (result, trades, _) = _engine.Run(strategy, candles);

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
            ExitConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":70}"""
        };

        var candles = GenerateCandles(300, 50000);
        var (result, trades, _) = _engine.Run(strategy, candles);

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
            EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":0}""",
            ExitConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":"<","Value":100}"""
        };

        var candles = GenerateCandles(300, 50000);
        var (_, _, analysis) = _engine.Run(strategy, candles);

        Assert.NotEmpty(analysis);

        var last = analysis[^1];
        var keys = new[] { "SMA_20", "SMA_50", "EMA_20", "MACD_LINE", "MACD_SIGNAL", "BB_UPPER", "BB_LOWER" };
        foreach (var key in keys)
        {
            Assert.True(last.IndicatorValues.ContainsKey(key), $"缺少指标: {key}");
            Assert.NotEqual(0, last.IndicatorValues[key]);
        }
    }

    [Fact]
    public void Run_VolatilityGrid_RangePctEntry_ProducesPositionAnalysis()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"operator":"","indicator":"RANGE_PCT","comparison":">=","value":1}""",
            ExitConditionJson = "{}",
            ExecutionRuleJson = """{"type":"volatility_grid","entryVolatilityPercent":1,"rebalancePercent":1,"basePositionSize":100,"maxPositionSize":600,"maxPyramidingLevels":5,"noStopLoss":true,"slippageTolerance":0.0005,"maxDailyLoss":200}"""
        };

        var candles = GenerateVolatileCandles(120, 100m);
        var (result, trades, analysis) = _engine.Run(strategy, candles, 1000m);

        Assert.NotEmpty(trades);
        Assert.Contains(analysis, a => a.Action == "enter");
        Assert.Contains(analysis, a => a.InPosition);
        Assert.Contains(analysis, a => a.PositionCost is > 0 && a.PositionValue is > 0);
    }

    [Fact]
    public void Run_VolatilityGrid_LowVolatility_NoEntry()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"operator":"","indicator":"RANGE_PCT","comparison":">=","value":5}""",
            ExitConditionJson = "{}",
            ExecutionRuleJson = """{"type":"volatility_grid","entryVolatilityPercent":5,"rebalancePercent":1,"basePositionSize":100,"maxPositionSize":600,"maxPyramidingLevels":5,"noStopLoss":true,"slippageTolerance":0.0005,"maxDailyLoss":200}"""
        };

        var candles = GenerateCandles(100, 50000);
        var (result, trades, _) = _engine.Run(strategy, candles, 1000m);

        Assert.Empty(trades);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void Run_VolatilityGrid_PriceBelowAvg_PyramidAddsPositions()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"operator":"","indicator":"RANGE_PCT","comparison":">=","value":1}""",
            ExitConditionJson = "{}",
            ExecutionRuleJson = """{"type":"volatility_grid","entryVolatilityPercent":1,"rebalancePercent":1,"basePositionSize":100,"maxPositionSize":600,"maxPyramidingLevels":3,"noStopLoss":true,"slippageTolerance":0.0005,"maxDailyLoss":200}"""
        };

        var candles = GeneratePyramidingCandles(120, 100m);
        var (result, trades, analysis) = _engine.Run(strategy, candles, 1000m);

        Assert.True(trades.Count >= 1);
        var enterCount = analysis.Count(a => a.Action == "enter");
        Assert.True(enterCount >= 2, $"入场次数 {enterCount} 应 >= 2（首单 + 至少一次加仓）");
    }

    [Fact]
    public void Run_VolatilityGrid_MaxPyramidLimit_PreventsOverEntry()
    {
        var strategy = new Strategy
        {
            EntryConditionJson = """{"operator":"","indicator":"RANGE_PCT","comparison":">=","value":1}""",
            ExitConditionJson = "{}",
            ExecutionRuleJson = """{"type":"volatility_grid","entryVolatilityPercent":1,"rebalancePercent":1,"basePositionSize":100,"maxPositionSize":600,"maxPyramidingLevels":1,"noStopLoss":true,"slippageTolerance":0.0005,"maxDailyLoss":200}"""
        };

        var candles = GeneratePyramidingCandles(150, 100m);
        var (result, trades, analysis) = _engine.Run(strategy, candles, 1000m);

        var enterCount = analysis.Count(a => a.Action == "enter");
        var inPositionCounts = analysis.Where(a => a.InPosition).Select(a => a.PositionQuantity ?? 0).ToList();
        var maxPositionQuantity = inPositionCounts.Count > 0 ? inPositionCounts.Max() : 0;

        Assert.True(enterCount >= 1, "至少应有 1 次入场");
        Assert.Contains(analysis, a => a.Action == "enter");
        Assert.Contains(analysis, a => a.InPosition);
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

    private static List<Candle> GenerateVolatileCandles(int count, decimal basePrice)
    {
        var random = new Random(42);
        var candles = new List<Candle>(count);
        var price = basePrice;

        for (var i = 0; i < count; i++)
        {
            var open = price;
            var high = open * 1.012m;
            var low = open * 0.998m;
            var close = i % 12 == 0 ? open * 0.985m : open * 1.002m;
            price = close;

            candles.Add(new Candle(
                DateTime.UtcNow.AddMinutes(-(count - i) * 15),
                open,
                high,
                low,
                close,
                1000 + i));
        }

        return candles;
    }

    private static List<Candle> GeneratePyramidingCandles(int count, decimal basePrice)
    {
        var candles = new List<Candle>(count);
        var price = basePrice;

        for (var i = 0; i < count; i++)
        {
            var open = price;
            var high = open * 1.015m;
            var low = open * 0.995m;

            if (i < 55)
                price = open * 1.003m;
            else if (i < 75)
                price = open * 0.993m;
            else if (i < 100)
                price = open * 0.994m;
            else
                price = open * 0.995m;

            if (i == 55)
            {
                high = open * 1.02m;
                low = open * 0.99m;
            }

            candles.Add(new Candle(
                DateTime.UtcNow.AddHours(-(count - i) * 2),
                open, high, low, price, 1000 + i));
        }

        return candles;
    }
}
