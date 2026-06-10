using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Rules.Engine;
using TradeX.Trading.Backtest;
using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

/// <summary>
/// 一致性测试: 同一策略 + 同一 K 线段, BacktestEngine 一次性跑出来的 trade 序列,
/// 应当与"模拟实盘评估器"逐根输入产生的 trade 序列完全相同 (除滑点/手续费, 当前两路都不模拟这些).
///
/// 模拟实盘评估器: 直接复用 IStrategyDecisionEngine + IIndicatorRegistry, 与 BacktestEngine 的核心评估同源.
/// 任何让两路 diverge 的改动都会让本套件失败.
/// </summary>
public class BacktestParityTests
{
    [Fact]
    public void Backtest_VsRealtimeReplay_SameTradeSequence()
    {
        var registry = new IndicatorRegistry();
        DependencyInjection.RegisterDefaults(registry, new IndicatorService());
        var decisionEngine = new StrategyDecisionEngine(new RuleEvaluator(new TriggerTracker()));
        var engine = new BacktestEngine(registry, decisionEngine);

        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(
                RuleSetJson.Leaf("SMA_20", "CA", 50000),
                RuleSetJson.Leaf("SMA_20", "CB", 50000))
        };

        var klines = BuildKlines(400);

        var (_, backtestTrades, _) = engine.Run(strategy, "BTCUSDT", klines);
        var realtimeTrades = ReplayAsRealtime(strategy, klines, registry, decisionEngine);

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
        var decisionEngine = new StrategyDecisionEngine(new RuleEvaluator(new TriggerTracker()));
        var engine = new BacktestEngine(registry, decisionEngine);

        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(
                RuleSetJson.Leaf("SMA_50", ">", 1.005m, "SMA_20"),
                RuleSetJson.Leaf("SMA_50", "<", 0.995m, "SMA_20"))
        };
        var klines = BuildKlines(300);

        var (_, backtestTrades, _) = engine.Run(strategy, "BTCUSDT", klines);
        var realtimeTrades = ReplayAsRealtime(strategy, klines, registry, decisionEngine);

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

    [Fact]
    public void ExecutionRule_WithFixedSize_UsesSpecifiedQuoteSize()
    {
        var registry = new IndicatorRegistry();
        DependencyInjection.RegisterDefaults(registry, new IndicatorService());
        var decisionEngine = new StrategyDecisionEngine(new RuleEvaluator(new TriggerTracker()));
        var engine = new BacktestEngine(registry, decisionEngine);

        // 规则集指定 size=100（固定金额），不传 positionSize（null）
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryOnly(RuleSetJson.True, """{"action":"buy","size":100,"sizeType":"fixed"}""")
        };

        var klines = BuildKlines(300);
        var (_, trades, _) = engine.Run(strategy, "BTCUSDT", klines, initialCapital: 10000m, positionSize: null);

        Assert.NotEmpty(trades);
        // 入场金额 = min(100, cash) = 100，数量 = 100 / entryPrice
        foreach (var trade in trades)
        {
            var fullMarginQty = 10000m / trade.EntryPrice;
            Assert.True(trade.Quantity < fullMarginQty,
                $"预期的部分仓位: Quantity={trade.Quantity} 应小于全仓={fullMarginQty:F4}");
            // size=100, quantity = 100 / entryPrice（假设无手续费）
            var expectedQty = Math.Round(100m / trade.EntryPrice, 8);
            Assert.True(Math.Abs(trade.Quantity - expectedQty) < 0.0001m,
                $"Quantity={trade.Quantity} 应接近 {expectedQty}, 差值={Math.Abs(trade.Quantity - expectedQty)}");
        }
    }

    // ──── 逐根输入的模拟实盘评估器（镜像 BacktestEngine 的单笔持仓行为）────
    private static List<BacktestTrade> ReplayAsRealtime(
        Strategy strategy,
        List<Kline> klines,
        IIndicatorRegistry registry,
        IStrategyDecisionEngine decisionEngine)
    {
        var trades = new List<BacktestTrade>();
        var prices = klines.Select(c => c.Close).ToArray();
        var volumes = klines.Select(c => (long)c.Volume).ToArray();

        var inPosition = false;
        var entryPrice = 0m;
        var entryIndex = 0;

        for (var i = 50; i < klines.Count; i++)
        {
            var window = prices[..(i + 1)];
            var prevWindow = prices[..i];
            var current = registry.ComputeAll(new KlineWindow(window, volumes[..(i + 1)],
                klines[i].Open, klines[i].High, klines[i].Low, klines[i].Close));
            var previous = registry.ComputeAll(new KlineWindow(prevWindow, volumes[..i],
                klines[i - 1].Open, klines[i - 1].High, klines[i - 1].Low, klines[i - 1].Close));

            var decision = decisionEngine.Decide(new StrategyDecisionInput(
                ExecutionRule: strategy.ExecutionRule,
                IndicatorValues: current,
                PreviousIndicatorValues: previous,
                CurrentPrice: klines[i].Close,
                AverageEntryPrice: entryPrice,
                QuantityHeld: inPosition ? 1m : 0m,
                LotCount: inPosition ? 1 : 0,
                ScopeKey: "backtest",
                EvaluationTime: klines[i].Timestamp));

            if (!inPosition && decision.Action == StrategyAction.EnterMarket)
            {
                inPosition = true;
                entryPrice = klines[i].Close;
                entryIndex = i;
            }
            else if (inPosition &&
                     (decision.Action == StrategyAction.ExitAll || decision.Action == StrategyAction.Reduce))
            {
                trades.Add(new BacktestTrade(entryIndex, i, klines[entryIndex].Timestamp, klines[i].Timestamp,
                    entryPrice, klines[i].Close, 0m, 0m, 0m));
                inPosition = false;
            }

            // 末根强制平掉剩余持仓（与 BacktestEngine 一致）
            if (i == klines.Count - 1 && inPosition)
            {
                trades.Add(new BacktestTrade(entryIndex, i, klines[entryIndex].Timestamp, klines[i].Timestamp,
                    entryPrice, klines[i].Close, 0m, 0m, 0m));
                inPosition = false;
            }
        }
        return trades;
    }

    private static List<Kline> BuildKlines(int count)
    {
        var rand = new Random(7);
        var result = new List<Kline>(count);
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
        {
            var trend = (decimal)Math.Sin(i / 25.0) * 2000;
            var noise = (decimal)(rand.NextDouble() - 0.5) * 400;
            var close = 50000m + trend + noise;
            var open = i > 0 ? result[^1].Close : close;
            var high = Math.Max(open, close) + Math.Abs(noise) * 0.5m;
            var low = Math.Min(open, close) - Math.Abs(noise) * 0.5m;
            result.Add(new Kline(t0.AddHours(i), open, high, low, close, (long)(rand.NextDouble() * 1000 + 100)));
        }
        return result;
    }
}
