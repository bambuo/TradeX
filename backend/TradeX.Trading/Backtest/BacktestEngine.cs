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
            return (CreateEmptyResult("数据不足，至少需要 50 根 K 线"), [], []);

        if (klines.Count > MaxKlines)
            return (CreateEmptyResult($"数据量过大，超过 {MaxKlines} 根 K 线上限"), [], []);

        List<BacktestTrade> trades = [];
        List<BacktestKlineAnalysis> analysis = [];
        var prices = klines.Select(c => c.Close).ToArray();
        var volumes = klines.Select(c => (long)c.Volume).ToArray();
        var entryPrice = 0m;
        var entryIndex = 0;
        var entryQuantity = 0m;
        var workedCapital = initialCapital;
        var inPosition = false;

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
                if (shouldEnter)
                {
                    inPosition = true;
                    action = "enter";
                    entryPrice = kline.Close;
                    entryIndex = i;
                    // positionSize 指定时按固定金额入场，否则全仓
                    entryQuantity = entryPrice > 0
                        ? (positionSize.HasValue ? positionSize.Value / entryPrice : workedCapital / entryPrice)
                        : 0m;
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
                    var pnlPercent = (exitPrice - entryPrice) / entryPrice * 100;

                    trades.Add(new BacktestTrade(
                        entryIndex, i,
                        klines[entryIndex].Timestamp, kline.Timestamp,
                        entryPrice, exitPrice, qty, pnl, pnlPercent));

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

            onAnalysis?.Invoke(analysis[^1]);
        }

        if (inPosition)
        {
            var lastIdx = prices.Length - 1;
            var exitPrice = klines[lastIdx].Close;
            var qty = entryQuantity;
            var pnl = (exitPrice - entryPrice) * qty;
            var pnlPercent = (exitPrice - entryPrice) / entryPrice * 100;
            trades.Add(new BacktestTrade(
                entryIndex, lastIdx,
                klines[entryIndex].Timestamp, klines[lastIdx].Timestamp,
                entryPrice, exitPrice, qty, pnl, pnlPercent));
        }

        var result = CalculateMetrics(trades, klines, prices);
        return (result, trades, analysis);
    }

    private static BacktestResult CalculateMetrics(
        IReadOnlyList<BacktestTrade> trades,
        IReadOnlyList<Candle> klines,
        decimal[] prices)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        if (trades.Count == 0)
            return new BacktestResult
            {
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

        var totalReturn = trades.Sum(t => t.PnLPercent);
        var totalDays = (klines[^1].Timestamp - klines[0].Timestamp).TotalDays;
        var annualizedReturn = 0m;
        if (totalDays > 0)
        {
            var baseVal = 1 + (double)Math.Max(-50, Math.Min(9999, totalReturn)) / 100;
            if (baseVal > 0)
                annualizedReturn = (decimal)Math.Max(-9999, Math.Min(9999, Math.Pow(baseVal, 365.0 / totalDays) - 1)) * 100;
        }

        var peak = prices[0];
        var maxDrawdown = 0m;
        foreach (var price in prices)
        {
            if (price > peak) peak = price;
            var dd = (peak - price) / peak * 100;
            if (dd > maxDrawdown) maxDrawdown = dd;
        }

        var avgReturn = trades.Count > 0 ? trades.Average(t => t.PnLPercent) : 0;
        var stdDev = trades.Count > 1
            ? (decimal)Math.Sqrt(Math.Max(0, Math.Min(1e10, trades.Average(t => Math.Pow((double)Math.Max(-9999, Math.Min(9999, t.PnLPercent - avgReturn)), 2)))))
            : 1;
        var sharpe = stdDev > 0 ? avgReturn / stdDev * (decimal)Math.Sqrt(365) : 0;

        var totalWin = trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
        var totalLoss = trades.Where(t => t.PnL <= 0).Sum(t => Math.Abs(t.PnL));
        var profitLossRatio = totalLoss > 0 ? totalWin / totalLoss : totalWin > 0 ? totalWin : 0;

        // 保持与历史 BacktestResult.Details 一致的字段名 (pnL/pnLPercent), 避免前端读取失败.
        // CamelCase 策略对 "PnL"/"PnLPercent" 只小写首字母, 输出即 pnL/pnLPercent.
        var details = JsonSerializer.Serialize(trades.Select(t => new
        {
            t.EnteredAt, t.ExitedAt, t.EntryPrice, t.ExitPrice,
            t.Quantity, t.PnL, t.PnLPercent
        }), jsonOptions);

        return new BacktestResult
        {
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

    private static BacktestResult CreateEmptyResult(string reason)
        => new()
        {
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
