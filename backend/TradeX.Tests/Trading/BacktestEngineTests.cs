using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Rules.Engine;
using TradeX.Trading;
using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

public class BacktestEngineTests
{
    private readonly BacktestEngine _engine;

    public BacktestEngineTests()
    {
        var reg = new IndicatorRegistry();
        TradeX.Indicators.DependencyInjection.RegisterDefaults(reg, new IndicatorService());
        _engine = new BacktestEngine(reg, new StrategyDecisionEngine(new RuleEvaluator(new TriggerTracker())));
    }

    [Fact]
    public void Run_EntryNeverTriggers_ReturnsZeroTrades()
    {
        // RSI > 100 永不成立 → 无入场 → 零交易
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryOnly(RuleSetJson.Leaf("RSI", ">", 100), """{"action":"buy"}""")
        };

        var klines = GenerateKlines(100, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", klines);

        Assert.Empty(trades);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void Run_EntryAlwaysTrue_ExitAlwaysTrue_ProducesTrades()
    {
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 0), RuleSetJson.Leaf("RSI", "<", 100))
        };

        var klines = GenerateKlines(200, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", klines);

        Assert.True(result.TotalTrades >= 1);
        Assert.NotEqual(0, result.TotalReturnPercent);
    }

    [Fact]
    public void Run_InsufficientData_ReturnsEmptyResult()
    {
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryOnly(RuleSetJson.Leaf("RSI", ">", 30), """{"action":"buy"}""")
        };

        var klines = GenerateKlines(10, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", klines);

        Assert.Equal(0, result.TotalTrades);
        Assert.Empty(trades);
    }

    [Fact]
    public void Run_ExitNeverTriggers_ClosesAtLastCandle()
    {
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 0), RuleSetJson.Leaf("RSI", ">", 100))
        };

        var klines = GenerateKlines(200, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", klines);

        Assert.True(result.TotalTrades >= 1);
        var lastTrade = trades[^1];
        Assert.Equal(klines[^1].Timestamp, lastTrade.ExitedAt);
    }

    [Fact]
    public void Run_CalculatesMetrics_Correctly()
    {
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 30), RuleSetJson.Leaf("RSI", "<", 70))
        };

        var klines = GenerateKlines(300, 50000);
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", klines);

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
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 0), RuleSetJson.Leaf("RSI", "<", 100))
        };

        var klines = GenerateKlines(300, 50000);
        var (_, _, analysis) = _engine.Run(strategy, "BTCUSDT", klines);

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
    public void Run_WithFee_ProducesLowerFinalValueThanZeroFee()
    {
        // 手续费应从权益中真实扣减：相同行情/策略下，含费回测期末权益严格低于无费
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 0), RuleSetJson.Leaf("RSI", "<", 100))
        };

        var klines = GenerateKlines(200, 50000);

        var (noFee, noFeeTrades, _) = _engine.Run(strategy, "BTCUSDT", klines, feeRate: 0m);
        var (withFee, _, _) = _engine.Run(strategy, "BTCUSDT", klines, feeRate: 0.001m);

        Assert.True(noFeeTrades.Count >= 1);
        Assert.True(withFee.FinalValue < noFee.FinalValue,
            $"含手续费期末权益({withFee.FinalValue})应低于无手续费({noFee.FinalValue})");
    }

    [Fact]
    public void Run_PyramidingRule_AddsLotsWhilePositioned()
    {
        // 持仓时再次买入（context=any 恒真）应加仓为多笔 lot，验证多笔持仓模型生效
        var rule = """
            {"code":"pyr","name":"金字塔","rules":[
              {"code":"add","name":"加仓","context":"any","priority":1,
               "when":{"operator":"TRUE"},
               "then":{"action":"buy","size":100,"sizeType":"fixed"},
               "constraints":{"maxPositions":3}}
            ]}
            """;
        var strategy = new Strategy { ExecutionRule = rule };

        var klines = GenerateKlines(120, 50000);
        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", klines, initialCapital: 100000m);

        // maxPositions=3 → 最多 3 笔同时持仓，末根全部平掉 → 至少 3 笔交易
        Assert.True(trades.Count >= 3, $"预期至少 3 笔加仓交易, 实际 {trades.Count}");
    }

    [Fact]
    public void Run_SellWithSize_ReducesMultipleLotsInOneDecision()
    {
        // 累计 3 笔 lot 后，sell size=150 应一次平掉 ≥2 笔（按 quote 金额 FIFO 减仓），
        // 而非旧的"只平一笔"。用 POSITION_COUNT 上下文指标精确控制时序。
        var rule = """
            {"code":"r","name":"r","rules":[
              {"code":"entry","name":"建仓","context":"noPosition","priority":1,
               "when":{"operator":"TRUE"},"then":{"action":"buy","size":100,"sizeType":"fixed"}},
              {"code":"add","name":"加仓","context":"hasPosition","priority":2,
               "when":{"indicator":"POSITION_COUNT","comparison":"<","value":3},
               "then":{"action":"buy","size":100,"sizeType":"fixed"}},
              {"code":"trim","name":"减仓","context":"hasPosition","priority":3,
               "when":{"indicator":"POSITION_COUNT","comparison":">=","value":3},
               "then":{"action":"sell","size":150,"sizeType":"fixed"}}
            ]}
            """;
        var strategy = new Strategy { ExecutionRule = rule };

        var klines = GenerateKlines(120, 50000);
        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", klines, initialCapital: 100000m);

        // 存在某个非末根 K 线，在同一决策中平掉了 ≥2 笔（同一 ExitedAt 有多笔成交）
        var lastTs = klines[^1].Timestamp;
        var multiCloseBars = trades
            .GroupBy(t => t.ExitedAt)
            .Where(g => g.Count() >= 2 && g.Key != lastTs)
            .ToList();
        Assert.NotEmpty(multiCloseBars);
    }

    private static List<Kline> GenerateKlines(int count, decimal basePrice)
    {
        var random = new Random(42);
        var klines = new List<Kline>(count);
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

            klines.Add(new Kline(
                DateTime.UtcNow.AddHours(-(count - i)),
                open, high, low, close,
                (long)(random.NextDouble() * 1000 + 100)));
        }

        return klines;
    }
}
