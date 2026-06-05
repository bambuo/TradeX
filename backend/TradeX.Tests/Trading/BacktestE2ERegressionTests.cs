using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading.Backtest;
using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

/// <summary>
/// 锁定 commit 9d57701 review 修复的端到端语义. 任何回归这些行为的改动应直接触发本套件失败.
/// 覆盖 7 个语义点: 空 JSON / CA CB 穿越 / Ref 相对比较 /
/// 损坏 JSON 兜底 / AND 空数组 / pnL JSON 字段名 / CancellationToken 中断.
/// </summary>
public class BacktestE2ERegressionTests
{
    private readonly BacktestEngine _engine = BuildEngine();

    private static BacktestEngine BuildEngine()
    {
        var reg = new IndicatorRegistry();
        TradeX.Indicators.DependencyInjection.RegisterDefaults(reg, new IndicatorService());
        return new BacktestEngine(reg, new ConditionEvaluator(new ConditionTreeEvaluator()));
    }

    [Fact]
    public void EmptyEntryCondition_ProducesZeroTrades()
    {
        // 空 JSON 不应触发交易, 防止每根 K 线开/平仓
        var strategy = new Strategy { EntryCondition = "{}", ExitCondition = "{}" };
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineCandles(200, 50000));

        Assert.Empty(trades);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void WhitespaceEntryCondition_ProducesZeroTrades()
    {
        var strategy = new Strategy { EntryCondition = "  ", ExitCondition = "{}" };
        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineCandles(200, 50000));

        Assert.Empty(trades);
    }

    [Fact]
    public void MalformedJson_DoesNotCrashEngine()
    {
        // 历史脏数据 / 手工改坏的策略不应让整轮回测崩溃
        var strategy = new Strategy { EntryCondition = "not json{", ExitCondition = "{}" };

        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineCandles(200, 50000));

        Assert.Empty(trades);
    }

    [Fact]
    public void LegacyCrossoverCodes_CA_CB_StillEvaluate()
    {
        // 历史策略保存的 Comparison='CA' / 'CB' 必须继续生效, 不能静默零交易
        var strategy = new Strategy
        {
            EntryCondition = """{"operator":"","indicator":"SMA_20","comparison":"CA","value":50000}""",
            ExitCondition = """{"operator":"","indicator":"SMA_20","comparison":"CB","value":50000}"""
        };

        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineCandles(400, 50000));

        Assert.NotEmpty(trades);
    }

    [Fact]
    public void CrossoverCodes_CA_CB_Evaluate()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"operator":"","indicator":"SMA_20","comparison":"CA","value":50000}""",
            ExitCondition = """{"operator":"","indicator":"SMA_20","comparison":"CB","value":50000}"""
        };

        var (_, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineCandles(400, 50000));

        Assert.NotEmpty(trades);
    }

    [Fact]
    public void RelativeComparison_WithRef_ComparesAgainstScaledIndicator()
    {
        // Ref 字段必须重新生效: 收盘 > SMA_20 * 1.001 (略高于均线)
        // 同等价格写死 50000 (literal) 会几乎恒为真; 用 Ref 后阈值会随均线浮动, 行为应有显著差异.
        var refStrategy = new Strategy
        {
            EntryCondition = """{"operator":"","indicator":"SMA_50","comparison":">","value":1.005,"ref":"SMA_20"}""",
            ExitCondition = """{"operator":"","indicator":"SMA_50","comparison":"<","value":0.995,"ref":"SMA_20"}"""
        };
        var literalStrategy = new Strategy
        {
            EntryCondition = """{"operator":"","indicator":"SMA_50","comparison":">","value":1.005}""",
            ExitCondition = """{"operator":"","indicator":"SMA_50","comparison":"<","value":0.995}"""
        };

        var candles = BuildSineCandles(400, 50000);
        var (_, refTrades, _) = _engine.Run(refStrategy, "BTCUSDT", candles);
        var (_, literalTrades, _) = _engine.Run(literalStrategy, "BTCUSDT", candles);

        // 字面比较 50000 > 1.005 恒真, literal 在第 50 根立即开仓且永不出场 → 1 trade
        Assert.Single(literalTrades);
        // Ref 模式有真正的均线穿越逻辑 → trade 数 / 时机不同
        Assert.NotEqual(literalTrades.Count == 1 && refTrades.Count == 1
            ? literalTrades[0].EnteredAt : DateTime.MinValue,
            refTrades.Count > 0 ? refTrades[0].EnteredAt : DateTime.MaxValue);
    }

    [Fact]
    public void AndOperator_EmptyConditions_TreatedAsVacuousTrue()
    {
        // 顶层 AND 空数组按"空真"语义: 仅作为内部树节点时为 true.
        // 引擎层面通过 ConditionEvaluator 解析 JSON, 空 JSON 路径独立返回 false, 不会走到这里.
        var tree = new ConditionTreeEvaluator();
        var node = new ConditionNode { Operator = "AND", Conditions = [] };

        Assert.True(tree.Evaluate(node, [], []));
    }

    [Fact]
    public void DetailsJson_PreservesLegacyFieldNames_pnL_pnLPercent()
    {
        // 前端依赖 details[i].pnL / pnLPercent 字段, 不可改名
        var strategy = new Strategy
        {
            EntryCondition = """{"operator":"","indicator":"RSI","comparison":">","value":0}""",
            ExitCondition = """{"operator":"","indicator":"RSI","comparison":"<","value":100}"""
        };
        var (result, trades, _) = _engine.Run(strategy, "BTCUSDT", BuildSineCandles(300, 50000));

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
            EntryCondition = """{"operator":"","indicator":"RSI","comparison":">","value":0}""",
            ExitCondition = """{"operator":"","indicator":"RSI","comparison":">","value":100}"""
        };

        var candles = BuildSineCandles(500, 50000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _engine.Run(strategy, "BTCUSDT", candles, ct: cts.Token));
    }

    [Fact]
    public void Run_CancellationTokenCancelledMidRun_StopsWithinFewIterations()
    {
        var strategy = new Strategy
        {
            EntryCondition = """{"operator":"","indicator":"RSI","comparison":">","value":0}""",
            ExitCondition = """{"operator":"","indicator":"RSI","comparison":">","value":100}"""
        };

        // 引擎只在每根 K 线开头检查 ct, 用 Action onAnalysis 注入计数器, 第 5 根之后触发取消
        var candles = BuildSineCandles(1000, 50000);
        using var cts = new CancellationTokenSource();
        var processed = 0;

        var ex = Record.Exception(() => _engine.Run(strategy, "BTCUSDT", candles,
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
    private static List<Candle> BuildSineCandles(int count, decimal basePrice)
    {
        var rand = new Random(42);
        var candles = new List<Candle>(count);
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
            candles.Add(new Candle(t0.AddHours(i), open, high, low, close, (long)(rand.NextDouble() * 1000 + 100)));
            price = close;
        }
        return candles;
    }
}
