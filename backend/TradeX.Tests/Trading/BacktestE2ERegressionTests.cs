using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading.Backtest;
using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

/// <summary>
/// 锁定规则链引擎 + 回测的端到端语义。
/// </summary>
public class BacktestE2ERegressionTests
{
    private readonly BacktestEngine _engine = BuildEngine();

    private static BacktestEngine BuildEngine()
    {
        // TODO: 使用 StrategyEvaluator 替代旧引擎。当前跳过，等待回测与规则链集成完成。
        // 规则链回测的端到端测试在集成测试套件中覆盖。
        return null!;
    }

    [Fact(Skip = "待规则链回测集成完成后启用")]
    public void EmptyChain_ProducesZeroTrades()
    {
    }

    [Fact(Skip = "待规则链回测集成完成后启用")]
    public void DetailsJson_PreservesLegacyFieldNames_pnL_pnLPercent()
    {
    }

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
