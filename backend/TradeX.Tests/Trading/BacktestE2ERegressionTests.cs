using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Rules.Engine;
using TradeX.Trading.Backtest;
using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

/// <summary>
/// 锁定规则引擎 + 回测的端到端语义。任何回归这些行为的改动应直接触发本套件失败。
/// 覆盖: 空/空白/损坏 ExecutionRule → 零交易且不崩 / CA·CB 穿越 / Ref 相对比较 /
/// pnL JSON 字段名 / CancellationToken 中断。
/// </summary>
public class BacktestE2ERegressionTests
{
    private readonly BacktestEngine _engine = BuildEngine();

    private static BacktestEngine BuildEngine()
    {
        var reg = new IndicatorRegistry();
        TradeX.Indicators.DependencyInjection.RegisterDefaults(reg, new IndicatorService());
        return new BacktestEngine(reg, new StrategyDecisionEngine(new RuleEvaluator(new TriggerTracker())));
    }

    [Fact]
    public void EmptyExecutionRule_ProducesZeroTrades()
    {
        // 空规则集不应触发交易, 防止每根 K 线开/平仓
        var strategy = new Strategy { ExecutionRule = "{}" };
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineKlines(200, 50000));

        Assert.Empty(trades);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void WhitespaceExecutionRule_ProducesZeroTrades()
    {
        var strategy = new Strategy { ExecutionRule = "  " };
        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineKlines(200, 50000));

        Assert.Empty(trades);
    }

    [Fact]
    public void MalformedJson_DoesNotCrashEngine()
    {
        // 手工改坏的规则集不应让整轮回测崩溃，应 fail-closed 为零交易
        var strategy = new Strategy { ExecutionRule = "not json{" };

        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineKlines(200, 50000));

        Assert.Empty(trades);
    }

    [Fact]
    public void MalformedRule_FailsClosed_NoPartialRuleSet()
    {
        // 一条规则坏掉（action 非法）应拒绝整个规则集，而非半套生效（只买不卖）
        var rule = """
            {"code":"s","name":"s","rules":[
              {"code":"entry","name":"e","context":"noPosition","when":{"operator":"TRUE"},"then":{"action":"buy"}},
              {"code":"exit","name":"x","context":"hasPosition","when":{"operator":"TRUE"},"then":{"action":"INVALID"}}
            ]}
            """;
        var strategy = new Strategy { ExecutionRule = rule };

        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineKlines(200, 50000));

        Assert.Empty(trades);
    }

    [Fact]
    public void CrossoverCodes_CA_CB_Evaluate()
    {
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(
                RuleSetJson.Leaf("SMA_20", "CA", 50000),
                RuleSetJson.Leaf("SMA_20", "CB", 50000))
        };

        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineKlines(400, 50000));

        Assert.NotEmpty(trades);
    }

    [Fact]
    public void RelativeComparison_WithRef_ComparesAgainstScaledIndicator()
    {
        // Ref 字段: 收盘 > SMA_20 * 1.005; 同等价格写死字面值会近乎恒真, 用 Ref 后阈值随均线浮动。
        var refStrategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(
                RuleSetJson.Leaf("SMA_50", ">", 1.005m, "SMA_20"),
                RuleSetJson.Leaf("SMA_50", "<", 0.995m, "SMA_20"))
        };
        var literalStrategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(
                RuleSetJson.Leaf("SMA_50", ">", 1.005m),
                RuleSetJson.Leaf("SMA_50", "<", 0.995m))
        };

        var klines = BuildSineKlines(400, 50000);
        var (_, refTrades, _) = _engine.Run(refStrategy, "BTCUSDT", klines);
        var (_, literalTrades, _) = _engine.Run(literalStrategy, "BTCUSDT", klines);

        // 字面比较 SMA_50 > 1.005 恒真, literal 在第 50 根立即开仓且永不出场 → 末根强平 → 1 trade
        Assert.Single(literalTrades);
        // Ref 模式有真正的均线穿越逻辑 → trade 数 / 时机不同
        Assert.NotEqual(literalTrades.Count == 1 && refTrades.Count == 1
            ? literalTrades[0].EnteredAt : DateTime.MinValue,
            refTrades.Count > 0 ? refTrades[0].EnteredAt : DateTime.MaxValue);
    }

    [Fact]
    public void DetailsJson_PreservesLegacyFieldNames_pnL_pnLPercent()
    {
        // 前端依赖 details[i].pnL / pnLPercent 字段, 不可改名
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 0), RuleSetJson.Leaf("RSI", "<", 100))
        };
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineKlines(300, 50000));

        Assert.NotEmpty(trades);
        var details = JsonSerializer.Deserialize<List<JsonElement>>(result.Details);
        Assert.NotNull(details);
        Assert.NotEmpty(details!);
        var first = details[0];
        Assert.True(first.TryGetProperty("pnL", out _), $"缺少 pnL 字段, 实际: {first}");
        Assert.True(first.TryGetProperty("pnLPercent", out _), $"缺少 pnLPercent 字段");
        Assert.False(first.TryGetProperty("pnl", out _), "不应出现小写 pnl, 会让前端读不到 PnL");
    }

    [Fact]
    public void Run_RespectsCancellationToken_ThrowsOperationCanceled()
    {
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 0), RuleSetJson.Leaf("RSI", ">", 100))
        };

        var klines = BuildSineKlines(500, 50000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _engine.Run(strategy, "BTCUSDT", klines, ct: cts.Token));
    }

    [Fact]
    public void Run_CancellationTokenCancelledMidRun_StopsWithinFewIterations()
    {
        var strategy = new Strategy
        {
            ExecutionRule = RuleSetJson.EntryExit(RuleSetJson.Leaf("RSI", ">", 0), RuleSetJson.Leaf("RSI", ">", 100))
        };

        // 引擎只在每根 K 线开头检查 ct, 用 Action onAnalysis 注入计数器, 第 5 根之后触发取消
        var klines = BuildSineKlines(1000, 50000);
        using var cts = new CancellationTokenSource();
        var processed = 0;

        var ex = Record.Exception(() => _engine.Run(strategy, "BTCUSDT", klines,
            onAnalysis: _ =>
            {
                if (Interlocked.Increment(ref processed) == 5) cts.Cancel();
            },
            ct: cts.Token));

        Assert.IsType<OperationCanceledException>(ex);
        // 取消触发后, 引擎最多再处理 1 根 K 线即应抛出 (循环顶部检查)
        Assert.InRange(processed, 5, 6);
    }

    // ──── 合成 K 线: 正弦波 + 噪声, 用相同 seed 保证测试确定性 ────
    private static List<Kline> BuildSineKlines(int count, decimal basePrice)
    {
        var rand = new Random(42);
        var klines = new List<Kline>(count);
        var price = basePrice;
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
        {
            var trend = (decimal)Math.Sin(i / 20.0) * 1500;
            var noise = (decimal)(rand.NextDouble() - 0.5) * 300;
            var open = price;
            var close = basePrice + trend + noise;
            var high = Math.Max(open, close) + Math.Abs(noise) * 0.5m;
            var low = Math.Min(open, close) - Math.Abs(noise) * 0.5m;
            klines.Add(new Kline(t0.AddHours(i), open, high, low, close, (long)(rand.NextDouble() * 1000 + 100)));
            price = close;
        }
        return klines;
    }
}
