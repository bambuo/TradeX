using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;

namespace TradeX.Trading.Backtest;

public class BacktestEngine(IIndicatorRegistry indicators, IConditionEvaluator conditionEvaluator)
{
    private const int MaxKlines = 100_000;

    public (BacktestResult Result, List<BacktestTrade> Trades, List<BacktestKlineAnalysis> Analysis) Run(
        Strategy strategy,
        string pair,
        IReadOnlyList<Candle> klines,
        decimal initialCapital = 1000m,
        decimal? positionSize = null,
        Action<BacktestKlineAnalysis>? onAnalysis = null,
        string? timeframe = null,
        CancellationToken ct = default)
    {
        if (klines.Count < 50)
            return (CreateEmptyResult("数据不足，至少需要 50 根 K 线", initialCapital), [], []);

        if (klines.Count > MaxKlines)
            return (CreateEmptyResult($"数据量过大，超过 {MaxKlines} 根 K 线上限", initialCapital), [], []);

        List<BacktestTrade> trades = [];
        List<BacktestKlineAnalysis> analysis = [];
        var prices = klines.Select(c => c.Close).ToArray();
        var volumes = klines.Select(c => (long)c.Volume).ToArray();
        var entryPrice = 0m;
        var entryIndex = 0;
        var entryQuantity = 0m;
        // 现金 + 持仓市值 = 账户权益。现金随平仓回笼，全仓模式下次入场即用最新现金 → 自然复利。
        var cash = initialCapital;
        var inPosition = false;
        // 每根 K 线的账户权益序列，用于回撤 / Sharpe（基于策略资金曲线而非标的价格）。
        List<decimal> equityCurve = [];

        for (var i = 50; i < prices.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var window = prices[..(i + 1)];
            var prevWindow = prices[..i];
            var volWindow = volumes[..(i + 1)];

            var ohlc = klines[i];
            var prevOhlc = klines[i - 1];
            var currentValues = indicators.ComputeAll(new KlineWindow(window, volWindow, ohlc.Open, ohlc.High, ohlc.Low, ohlc.Close));
            var previousValues = indicators.ComputeAll(new KlineWindow(prevWindow, volumes[..i], prevOhlc.Open, prevOhlc.High, prevOhlc.Low, prevOhlc.Close));

            var kline = ohlc;
            var action = "none";

            if (!inPosition)
            {
                var shouldEnter = conditionEvaluator.Evaluate(strategy.EntryCondition, currentValues, previousValues);
                if (shouldEnter && kline.Close > 0)
                {
                    inPosition = true;
                    action = "enter";
                    entryPrice = kline.Close;
                    entryIndex = i;
                    // positionSize 指定时按固定金额（不超过可用现金）入场，否则全仓投入当前现金（复利）
                    var capitalToUse = positionSize.HasValue ? Math.Min(positionSize.Value, cash) : cash;
                    entryQuantity = capitalToUse / entryPrice;
                    cash -= entryQuantity * entryPrice;
                }

                analysis.Add(new BacktestKlineAnalysis(
                    i, kline.Timestamp, kline.Open, kline.High, kline.Low, kline.Close, kline.Volume,
                    currentValues, shouldEnter, null, inPosition, action,
                    entryPrice > 0 ? entryPrice : null,
                    entryQuantity > 0 ? entryQuantity : null,
                    entryQuantity > 0 ? entryPrice * entryQuantity : null,
                    entryQuantity > 0 ? kline.Close * entryQuantity : null,
                    entryQuantity > 0 ? (kline.Close - entryPrice) * entryQuantity : null,
                    entryPrice > 0 ? (kline.Close - entryPrice) / entryPrice * 100m : null));
            }
            else
            {
                var shouldExit = conditionEvaluator.Evaluate(strategy.ExitCondition, currentValues, previousValues);
                var avgEntryForRow = entryPrice;
                var quantityForRow = entryQuantity;
                decimal? costForRow = quantityForRow > 0 ? avgEntryForRow * quantityForRow : null;
                decimal? valueForRow = quantityForRow > 0 ? kline.Close * quantityForRow : null;
                decimal? pnlForRow = quantityForRow > 0 ? (kline.Close - avgEntryForRow) * quantityForRow : null;
                decimal? pnlPercentForRow = avgEntryForRow > 0 ? (kline.Close - avgEntryForRow) / avgEntryForRow * 100m : null;

                if (shouldExit || i == prices.Length - 1)
                {
                    var exitPrice = kline.Close;
                    var qty = entryQuantity;
                    var pnl = (exitPrice - entryPrice) * qty;
                    var pnlPercent = entryPrice > 0 ? (exitPrice - entryPrice) / entryPrice * 100 : 0;

                    trades.Add(new BacktestTrade(
                        entryIndex, i,
                        klines[entryIndex].Timestamp, kline.Timestamp,
                        entryPrice, exitPrice, qty, pnl, pnlPercent));

                    cash += qty * exitPrice; // 平仓资金回笼，驱动复利
                    inPosition = false;
                    action = "exit";
                    entryQuantity = 0m;
                }

                analysis.Add(new BacktestKlineAnalysis(
                    i, kline.Timestamp, kline.Open, kline.High, kline.Low, kline.Close, kline.Volume,
                    currentValues, null, shouldExit, quantityForRow > 0, action,
                    avgEntryForRow > 0 ? avgEntryForRow : null,
                    quantityForRow > 0 ? quantityForRow : null,
                    costForRow,
                    valueForRow,
                    pnlForRow,
                    pnlPercentForRow));
            }

            // 账户权益 = 现金 + 当前持仓市值
            equityCurve.Add(cash + (inPosition ? entryQuantity * kline.Close : 0m));

            onAnalysis?.Invoke(analysis[^1]);
        }

        var finalEquity = equityCurve.Count > 0 ? equityCurve[^1] : initialCapital;
        var result = CalculateMetrics(trades, klines, equityCurve, initialCapital, finalEquity, timeframe);
        return (result, trades, analysis);
    }

    private static BacktestResult CalculateMetrics(
        IReadOnlyList<BacktestTrade> trades,
        IReadOnlyList<Candle> klines,
        IReadOnlyList<decimal> equityCurve,
        decimal initialCapital,
        decimal finalEquity,
        string? timeframe)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        if (trades.Count == 0)
            return new BacktestResult
            {
                InitialCapital = initialCapital,
                FinalValue = initialCapital,
                TotalReturnPercent = 0,
                AnnualizedReturnPercent = 0,
                MaxDrawdownPercent = 0,
                WinRate = 0,
                TotalTrades = 0,
                SharpeRatio = 0,
                ProfitLossRatio = 0,
                Details = "{\"message\":\"无交易产生\"}"
            };

        var wins = trades.Count(t => t.PnL > 0);
        var winRate = (decimal)wins / trades.Count * 100;

        // 总收益基于账户权益（期末/期初），而非各笔百分比简单累加
        var totalReturn = initialCapital > 0 ? (finalEquity - initialCapital) / initialCapital * 100 : 0;

        var totalDays = (klines[^1].Timestamp - klines[0].Timestamp).TotalDays;
        var annualizedReturn = 0m;
        if (totalDays > 0 && initialCapital > 0 && finalEquity > 0)
        {
            var growth = (double)(finalEquity / initialCapital);
            annualizedReturn = (decimal)Math.Max(-9999, Math.Min(9999, Math.Pow(growth, 365.0 / totalDays) - 1)) * 100;
        }

        // 最大回撤基于账户权益曲线（策略真实回撤），而非标的价格
        var maxDrawdown = ComputeMaxDrawdown(equityCurve);

        // Sharpe：账户权益的逐根收益率，按 timeframe 推导的年化周期数缩放
        var sharpe = ComputeSharpe(equityCurve, timeframe);

        var totalWin = trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
        var totalLoss = trades.Where(t => t.PnL <= 0).Sum(t => Math.Abs(t.PnL));
        var profitLossRatio = totalLoss > 0 ? totalWin / totalLoss : totalWin > 0 ? totalWin : 0;

        // 保持与历史 BacktestResult.Details 一致的字段名 (pnL/pnLPercent), 避免前端读取失败.
        var details = JsonSerializer.Serialize(trades.Select(t => new
        {
            t.EnteredAt, t.ExitedAt, t.EntryPrice, t.ExitPrice,
            t.Quantity, t.PnL, t.PnLPercent
        }), jsonOptions);

        return new BacktestResult
        {
            InitialCapital = initialCapital,
            FinalValue = Math.Round(finalEquity, 2),
            TotalReturnPercent = Math.Round(totalReturn, 2),
            AnnualizedReturnPercent = Math.Round(annualizedReturn, 2),
            MaxDrawdownPercent = Math.Round(maxDrawdown, 2),
            WinRate = Math.Round(winRate, 1),
            TotalTrades = trades.Count,
            SharpeRatio = Math.Round(sharpe, 2),
            ProfitLossRatio = Math.Round(profitLossRatio, 2),
            Details = details
        };
    }

    private static decimal ComputeMaxDrawdown(IReadOnlyList<decimal> equityCurve)
    {
        if (equityCurve.Count == 0) return 0m;
        var peak = equityCurve[0];
        var maxDrawdown = 0m;
        foreach (var equity in equityCurve)
        {
            if (equity > peak) peak = equity;
            if (peak > 0)
            {
                var dd = (peak - equity) / peak * 100;
                if (dd > maxDrawdown) maxDrawdown = dd;
            }
        }
        return maxDrawdown;
    }

    private static decimal ComputeSharpe(IReadOnlyList<decimal> equityCurve, string? timeframe)
    {
        if (equityCurve.Count < 3) return 0m;

        var returns = new List<double>(equityCurve.Count - 1);
        for (var i = 1; i < equityCurve.Count; i++)
        {
            var prev = (double)equityCurve[i - 1];
            if (prev <= 0) continue;
            returns.Add((double)equityCurve[i] / prev - 1);
        }
        if (returns.Count < 2) return 0m;

        var mean = returns.Average();
        var variance = returns.Average(r => Math.Pow(r - mean, 2));
        var stdDev = Math.Sqrt(Math.Max(0, variance));
        if (stdDev <= 0) return 0m;

        var periodsPerYear = PeriodsPerYear(timeframe);
        var sharpe = mean / stdDev * Math.Sqrt(periodsPerYear);
        return (decimal)Math.Max(-9999, Math.Min(9999, sharpe));
    }

    // 一年内该周期的 K 线根数，用于把逐根收益率年化
    private static double PeriodsPerYear(string? timeframe) => timeframe switch
    {
        "1m" => 525_600,
        "5m" => 105_120,
        "15m" => 35_040,
        "30m" => 17_520,
        "1h" => 8_760,
        "4h" => 2_190,
        "1d" => 365,
        _ => 365
    };

    private static BacktestResult CreateEmptyResult(string reason, decimal initialCapital)
        => new()
        {
            InitialCapital = initialCapital,
            FinalValue = initialCapital,
            TotalReturnPercent = 0,
            AnnualizedReturnPercent = 0,
            MaxDrawdownPercent = 0,
            WinRate = 0,
            TotalTrades = 0,
            SharpeRatio = 0,
            ProfitLossRatio = 0,
            Details = $"{{\"message\":\"{reason}\"}}"
        };
}
