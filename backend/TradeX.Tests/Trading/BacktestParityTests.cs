using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading.Backtest;
using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

/// <summary>
/// 一致性测试: 同一策略 + 同一 K 线段, BacktestEngine 一次性跑出来的 trade 序列,
/// 应当与"模拟实盘评估器"逐根输入产生的 trade 序列完全相同 (除滑点/手续费, 当前两路都不模拟这些).
///
/// 模拟实盘评估器: 直接复用 IConditionEvaluator + IIndicatorRegistry, 与 BacktestEngine 的核心评估同源.
/// 任何让两路 diverge 的改动 (例如只在回测里给指标传不同窗口) 都会让本套件失败.
/// </summary>
public class BacktestParityTests
{
    [Fact]
    public void Backtest_VsRealtimeReplay_SameTradeSequence()
    {
        var registry = new IndicatorRegistry();
        DependencyInjection.RegisterDefaults(registry, new IndicatorService());
        var treeEval = new ConditionTreeEvaluator();
        var condEval = new ConditionEvaluator(treeEval);
        var engine = new BacktestEngine(registry, condEval);

        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"SMA_20","Comparison":"CrossAbove","Value":50000}""",
            ExitCondition = """{"Operator":"","Indicator":"SMA_20","Comparison":"CrossBelow","Value":50000}"""
        };

        var candles = BuildCandles(400);

        var (_, backtestTrades, _) = engine.Run(strategy, "BTCUSDT", candles);
        var realtimeTrades = ReplayAsRealtime(strategy, candles, registry, condEval);

        Assert.Equal(backtestTrades.Count, realtimeTrades.Count);
        for (var i = 0; i < backtestTrades.Count; i++)
        {
            Assert.Equal(backtestTrades[i].EnteredAt, realtimeTrades[i].EnteredAt);
            Assert.Equal(backtestTrades[i].ExitedAt, realtimeTrades[i].ExitedAt);
            Assert.Equal(backtestTrades[i].EntryPrice, realtimeTrades[i].EntryPrice);
            Assert.Equal(backtestTrades[i].ExitPrice, realtimeTrades[i].ExitPrice);
        }
    }

    [Fact]
    public void Backtest_VsRealtimeReplay_WithRelativeRef_SameTrades()
    {
        var registry = new IndicatorRegistry();
        DependencyInjection.RegisterDefaults(registry, new IndicatorService());
        var condEval = new ConditionEvaluator(new ConditionTreeEvaluator());
        var engine = new BacktestEngine(registry, condEval);

        var strategy = new Strategy
        {
            EntryCondition = """{"Operator":"","Indicator":"SMA_50","Comparison":">","Value":1.005,"Ref":"SMA_20"}""",
            ExitCondition  = """{"Operator":"","Indicator":"SMA_50","Comparison":"<","Value":0.995,"Ref":"SMA_20"}"""
        };
        var candles = BuildCandles(300);

        var (_, backtestTrades, _) = engine.Run(strategy, "BTCUSDT", candles);
        var realtimeTrades = ReplayAsRealtime(strategy, candles, registry, condEval);

        Assert.Equal(backtestTrades.Count, realtimeTrades.Count);
        for (var i = 0; i < backtestTrades.Count; i++)
            Assert.Equal(backtestTrades[i].EnteredAt, realtimeTrades[i].EnteredAt);
    }

    [Fact]
    public void FixedClock_AdvancesDeterministically()
    {
        // 锁定 IClock 抽象的语义, 防止后续 SystemClock 被误改成默认值或测试时漂移
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FixedClock(t0);
        Assert.Equal(t0, clock.UtcNow);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(t0.AddMinutes(5), clock.UtcNow);
    }

    // ──── 逐根输入的模拟实盘评估器 ────
    private static List<BacktestTrade> ReplayAsRealtime(
        Strategy strategy,
        List<Candle> candles,
        IIndicatorRegistry registry,
        IConditionEvaluator condEval)
    {
        var trades = new List<BacktestTrade>();
        var prices = candles.Select(c => c.Close).ToArray();
        var volumes = candles.Select(c => (long)c.Volume).ToArray();

        var inPosition = false;
        var entryPrice = 0m;
        var entryIndex = 0;

        for (var i = 50; i < candles.Count; i++)
        {
            var window = prices[..(i + 1)];
            var prevWindow = prices[..i];
            var current = registry.ComputeAll(new KlineWindow(window, volumes[..(i + 1)],
                candles[i].Open, candles[i].High, candles[i].Low, candles[i].Close));
            var previous = registry.ComputeAll(new KlineWindow(prevWindow, volumes[..i],
                candles[i - 1].Open, candles[i - 1].High, candles[i - 1].Low, candles[i - 1].Close));

            if (!inPosition)
            {
                if (condEval.Evaluate(strategy.EntryCondition, current, previous))
                {
                    inPosition = true;
                    entryPrice = candles[i].Close;
                    entryIndex = i;
                }
            }
            else
            {
                var shouldExit = condEval.Evaluate(strategy.ExitCondition, current, previous);
                if (shouldExit || i == candles.Count - 1)
                {
                    trades.Add(new BacktestTrade(entryIndex, i, candles[entryIndex].Timestamp, candles[i].Timestamp,
                        entryPrice, candles[i].Close, 0m, 0m, 0m));
                    inPosition = false;
                }
            }
        }
        return trades;
    }

    private static List<Candle> BuildCandles(int count)
    {
        var rand = new Random(7);
        var result = new List<Candle>(count);
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
        {
            var trend = (decimal)Math.Sin(i / 25.0) * 2000;
            var noise = (decimal)(rand.NextDouble() - 0.5) * 400;
            var close = 50000m + trend + noise;
            var open = i > 0 ? result[^1].Close : close;
            var high = Math.Max(open, close) + Math.Abs(noise) * 0.5m;
            var low = Math.Min(open, close) - Math.Abs(noise) * 0.5m;
            result.Add(new Candle(t0.AddHours(i), open, high, low, close, (long)(rand.NextDouble() * 1000 + 100)));
        }
        return result;
    }
}
