using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;

namespace TradeX.Trading;

public class BacktestEngine(
    IIndicatorService indicatorService,
    IConditionEvaluator conditionEvaluator)
{
    public (BacktestResult Result, List<BacktestTrade> Trades, List<BacktestCandleAnalysis> Analysis) Run(
        Strategy strategy,
        IReadOnlyList<Candle> candles,
        decimal initialCapital = 1000m,
        Action<BacktestCandleAnalysis>? onAnalysis = null)
    {
        if (candles.Count < 50)
            return (CreateEmptyResult("数据不足，至少需要 50 根 K 线"), [], []);

        var trades = new List<BacktestTrade>();
        var analysis = new List<BacktestCandleAnalysis>();
        var prices = candles.Select(c => c.Close).ToArray();
        var volumes = candles.Select(c => (long)c.Volume).ToArray();
        var entryPrice = 0m;
        var entryIndex = 0;
        var workedCapital = initialCapital;
        var inPosition = false;

        for (var i = 50; i < prices.Length; i++)
        {
            var window = prices[..(i + 1)];
            var prevWindow = prices[..i];
            var volWindow = volumes[..(i + 1)];

            Dictionary<string, decimal> currentValues = new()
            {
                ["RSI"] = indicatorService.CalculateRsi(window),
                ["SMA_20"] = indicatorService.CalculateSma(window, 20),
                ["SMA_50"] = indicatorService.CalculateSma(window, 50),
                ["EMA_20"] = indicatorService.CalculateEma(window, 20),
                ["MACD_LINE"] = indicatorService.CalculateMacd(window).MacdLine,
                ["MACD_SIGNAL"] = indicatorService.CalculateMacd(window).SignalLine,
                ["BB_UPPER"] = indicatorService.CalculateBollingerBands(window).UpperBand,
                ["BB_LOWER"] = indicatorService.CalculateBollingerBands(window).LowerBand,
                ["OBV"] = indicatorService.CalculateObv(prices[..(i + 1)], volWindow),
                ["VOLUME_SMA"] = indicatorService.CalculateVolumeSma(volWindow),
            };

            Dictionary<string, decimal> previousValues = new()
            {
                ["RSI"] = indicatorService.CalculateRsi(prevWindow),
                ["SMA_20"] = indicatorService.CalculateSma(prevWindow, 20),
                ["SMA_50"] = indicatorService.CalculateSma(prevWindow, 50),
                ["EMA_20"] = indicatorService.CalculateEma(prevWindow, 20),
                ["MACD_LINE"] = indicatorService.CalculateMacd(prevWindow).MacdLine,
                ["MACD_SIGNAL"] = indicatorService.CalculateMacd(prevWindow).SignalLine,
            };

            var candle = candles[i];
            var action = "none";

            if (!inPosition)
            {
                var shouldEnter = conditionEvaluator.Evaluate(strategy.EntryConditionJson, currentValues, previousValues);
                if (shouldEnter)
                {
                    inPosition = true;
                    action = "enter";
                    entryPrice = candle.Close;
                    entryIndex = i;
                }

                analysis.Add(new BacktestCandleAnalysis(
                    i, candle.Timestamp, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
                    currentValues, shouldEnter, null, inPosition, action));
            }
            else
            {
                var shouldExit = conditionEvaluator.Evaluate(strategy.ExitConditionJson, currentValues, previousValues);
                if (shouldExit || i == prices.Length - 1)
                {
                    var exitPrice = candle.Close;
                    var qty = workedCapital / entryPrice;
                    var pnl = (exitPrice - entryPrice) * qty;
                    var pnlPercent = (exitPrice - entryPrice) / entryPrice * 100;

                    trades.Add(new BacktestTrade(
                        entryIndex, i,
                        candles[entryIndex].Timestamp, candle.Timestamp,
                        entryPrice, exitPrice, qty, pnl, pnlPercent));

                    inPosition = false;
                    action = "exit";
                }

                analysis.Add(new BacktestCandleAnalysis(
                    i, candle.Timestamp, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
                    currentValues, null, shouldExit, inPosition, action));
            }

            onAnalysis?.Invoke(analysis[^1]);
        }

        if (inPosition)
        {
            var lastIdx = prices.Length - 1;
            var exitPrice = candles[lastIdx].Close;
            var qty = workedCapital / entryPrice;
            var pnl = (exitPrice - entryPrice) * qty;
            var pnlPercent = (exitPrice - entryPrice) / entryPrice * 100;
            trades.Add(new BacktestTrade(
                entryIndex, lastIdx,
                candles[entryIndex].Timestamp, candles[lastIdx].Timestamp,
                entryPrice, exitPrice, qty, pnl, pnlPercent));
        }

        var result = CalculateMetrics(trades, candles, prices, analysis);
        return (result, trades, analysis);
    }

    private static BacktestResult CalculateMetrics(
        List<BacktestTrade> trades,
        IReadOnlyList<Candle> candles,
        decimal[] prices,
        List<BacktestCandleAnalysis> analysis)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var analysisJson = JsonSerializer.Serialize(analysis.Select(a => new
        {
            a.Index, a.Timestamp, a.Open, a.High, a.Low, a.Close, a.Volume,
            indicators = a.IndicatorValues,
            entry = a.EntryConditionResult,
            exit = a.ExitConditionResult,
            inPosition = a.InPosition,
            a.Action
        }), jsonOptions);

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
                DetailJson = $"{{\"message\":\"无交易产生\"}}",
                AnalysisJson = analysisJson
            };

        var wins = trades.Count(t => t.Pnl > 0);
        var winRate = (decimal)wins / trades.Count * 100;

        var totalReturn = trades.Sum(t => t.PnlPercent);
        var totalDays = (candles[^1].Timestamp - candles[0].Timestamp).TotalDays;
        var annualizedReturn = totalDays > 0
            ? (decimal)(Math.Pow(1 + (double)totalReturn / 100, 365.0 / totalDays) - 1) * 100
            : 0;

        var peak = prices[0];
        var maxDrawdown = 0m;
        foreach (var price in prices)
        {
            if (price > peak) peak = price;
            var dd = (peak - price) / peak * 100;
            if (dd > maxDrawdown) maxDrawdown = dd;
        }

        var avgReturn = trades.Count > 0 ? trades.Average(t => t.PnlPercent) : 0;
        var stdDev = trades.Count > 1
            ? (decimal)Math.Sqrt(trades.Average(t => Math.Pow((double)(t.PnlPercent - avgReturn), 2)))
            : 1;
        var sharpe = stdDev > 0 ? avgReturn / stdDev * (decimal)Math.Sqrt(365) : 0;

        var totalWin = trades.Where(t => t.Pnl > 0).Sum(t => t.Pnl);
        var totalLoss = trades.Where(t => t.Pnl <= 0).Sum(t => Math.Abs(t.Pnl));
        var profitLossRatio = totalLoss > 0 ? totalWin / totalLoss : totalWin > 0 ? totalWin : 0;

        var detailJson = JsonSerializer.Serialize(trades.Select(t => new
        {
            t.EntryTime, t.ExitTime, t.EntryPrice, t.ExitPrice,
            t.Quantity, t.Pnl, t.PnlPercent
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
            DetailJson = detailJson,
            AnalysisJson = analysisJson
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
            DetailJson = $"{{\"message\":\"{reason}\"}}",
            AnalysisJson = "[]"
        };
}
