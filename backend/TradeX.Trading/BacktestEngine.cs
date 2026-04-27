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
        Action<BacktestCandleAnalysis>? onAnalysis = null,
        string? timeframe = null)
    {
        if (candles.Count < 50)
            return (CreateEmptyResult("数据不足，至少需要 50 根 K 线"), [], []);

        var volatilityRule = VolatilityGridExecutionRuleParser.TryParse(strategy.ExecutionRuleJson);
        if (volatilityRule is not null)
            return RunVolatilityGrid(strategy, candles, volatilityRule, initialCapital, onAnalysis, timeframe);

        var trades = new List<BacktestTrade>();
        var analysis = new List<BacktestCandleAnalysis>();
        var prices = candles.Select(c => c.Close).ToArray();
        var volumes = candles.Select(c => (long)c.Volume).ToArray();
        var entryPrice = 0m;
        var entryIndex = 0;
        var entryQuantity = 0m;
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
                ["RANGE_PCT"] = candles[i].Open > 0 ? (candles[i].High - candles[i].Low) / candles[i].Open * 100m : 0m,
            };

            Dictionary<string, decimal> previousValues = new()
            {
                ["RSI"] = indicatorService.CalculateRsi(prevWindow),
                ["SMA_20"] = indicatorService.CalculateSma(prevWindow, 20),
                ["SMA_50"] = indicatorService.CalculateSma(prevWindow, 50),
                ["EMA_20"] = indicatorService.CalculateEma(prevWindow, 20),
                ["MACD_LINE"] = indicatorService.CalculateMacd(prevWindow).MacdLine,
                ["MACD_SIGNAL"] = indicatorService.CalculateMacd(prevWindow).SignalLine,
                ["RANGE_PCT"] = candles[i - 1].Open > 0 ? (candles[i - 1].High - candles[i - 1].Low) / candles[i - 1].Open * 100m : 0m,
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
                    entryQuantity = entryPrice > 0 ? workedCapital / entryPrice : 0m;
                }

                analysis.Add(new BacktestCandleAnalysis(
                    i, candle.Timestamp, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
                    currentValues, shouldEnter, null, inPosition, action,
                    entryPrice > 0 ? entryPrice : null,
                    entryQuantity > 0 ? entryQuantity : null,
                    entryQuantity > 0 ? entryPrice * entryQuantity : null,
                    entryQuantity > 0 ? candle.Close * entryQuantity : null,
                    entryQuantity > 0 ? (candle.Close - entryPrice) * entryQuantity : null,
                    entryPrice > 0 ? (candle.Close - entryPrice) / entryPrice * 100m : null));
            }
            else
            {
                var shouldExit = conditionEvaluator.Evaluate(strategy.ExitConditionJson, currentValues, previousValues);
                var avgEntryForRow = entryPrice;
                var quantityForRow = entryQuantity;
                decimal? costForRow = quantityForRow > 0 ? avgEntryForRow * quantityForRow : null;
                decimal? valueForRow = quantityForRow > 0 ? candle.Close * quantityForRow : null;
                decimal? pnlForRow = quantityForRow > 0 ? (candle.Close - avgEntryForRow) * quantityForRow : null;
                decimal? pnlPercentForRow = avgEntryForRow > 0 ? (candle.Close - avgEntryForRow) / avgEntryForRow * 100m : null;

                if (shouldExit || i == prices.Length - 1)
                {
                    var exitPrice = candle.Close;
                    var qty = entryQuantity;
                    var pnl = (exitPrice - entryPrice) * qty;
                    var pnlPercent = (exitPrice - entryPrice) / entryPrice * 100;

                    trades.Add(new BacktestTrade(
                        entryIndex, i,
                        candles[entryIndex].Timestamp, candle.Timestamp,
                        entryPrice, exitPrice, qty, pnl, pnlPercent));

                    inPosition = false;
                    action = "exit";
                    entryQuantity = 0m;
                }

                analysis.Add(new BacktestCandleAnalysis(
                    i, candle.Timestamp, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
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

    private (BacktestResult Result, List<BacktestTrade> Trades, List<BacktestCandleAnalysis> Analysis) RunVolatilityGrid(
        Strategy strategy,
        IReadOnlyList<Candle> candles,
        VolatilityGridExecutionRule rule,
        decimal initialCapital,
        Action<BacktestCandleAnalysis>? onAnalysis,
        string? timeframe = null)
    {
        var trades = new List<BacktestTrade>();
        var analysis = new List<BacktestCandleAnalysis>();
        var prices = candles.Select(c => c.Close).ToArray();
        var volumes = candles.Select(c => (long)c.Volume).ToArray();
        var openLegs = new List<OpenLeg>();
        var remainingCapital = initialCapital;

        for (var i = 50; i < prices.Length; i++)
        {
            var window = prices[..(i + 1)];
            var prevWindow = prices[..i];
            var volWindow = volumes[..(i + 1)];
            var candle = candles[i];

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
                ["RANGE_PCT"] = candle.Open > 0 ? (candle.High - candle.Low) / candle.Open * 100m : 0m,
            };

            Dictionary<string, decimal> previousValues = new()
            {
                ["RSI"] = indicatorService.CalculateRsi(prevWindow),
                ["SMA_20"] = indicatorService.CalculateSma(prevWindow, 20),
                ["SMA_50"] = indicatorService.CalculateSma(prevWindow, 50),
                ["EMA_20"] = indicatorService.CalculateEma(prevWindow, 20),
                ["MACD_LINE"] = indicatorService.CalculateMacd(prevWindow).MacdLine,
                ["MACD_SIGNAL"] = indicatorService.CalculateMacd(prevWindow).SignalLine,
                ["RANGE_PCT"] = candles[i - 1].Open > 0 ? (candles[i - 1].High - candles[i - 1].Low) / candles[i - 1].Open * 100m : 0m,
            };

            var rangePct = currentValues["RANGE_PCT"];
            var hasEntryCondition = !string.IsNullOrWhiteSpace(strategy.EntryConditionJson) && strategy.EntryConditionJson != "{}";
            var hasOpenPosition = openLegs.Count > 0;
            var avgEntry = CalculateAverageEntry(openLegs);
            var positionQuantity = openLegs.Sum(l => l.Quantity);
            var positionCost = openLegs.Sum(l => l.EntryPrice * l.Quantity);
            var canPyramid = openLegs.Count < rule.MaxPyramidingLevels + 1;

            var shouldEnter = !hasOpenPosition
                ? CheckWindowedVolatility(candles, i, timeframe, rule.EntryVolatilityPercent, candle.Close)
                : canPyramid && avgEntry > 0 && candle.Close <= avgEntry * (1 - rule.RebalancePercent / 100m);

            var shouldExit = hasOpenPosition
                && avgEntry > 0
                && candle.Close >= avgEntry * (1 + rule.RebalancePercent / 100m);

            var action = "none";
            decimal? rowAvgEntry = null;
            decimal? rowPositionQuantity = null;
            decimal? rowPositionCost = null;
            decimal? rowPositionValue = null;
            decimal? rowPositionPnl = null;
            decimal? rowPositionPnlPercent = null;

            if (shouldEnter && remainingCapital > 0)
            {
                var budget = Math.Min(rule.BasePositionSize, remainingCapital);
                var qty = candle.Close > 0 ? budget / candle.Close : 0m;
                if (qty > 0)
                {
                    openLegs.Add(new OpenLeg(i, candle.Timestamp, candle.Close, qty));
                    remainingCapital -= budget;
                    action = "enter";
                }
            }
            else if (shouldExit && openLegs.Count > 0)
            {
                rowAvgEntry = avgEntry > 0 ? avgEntry : null;
                rowPositionQuantity = positionQuantity > 0 ? positionQuantity : null;
                rowPositionCost = positionCost > 0 ? positionCost : null;
                rowPositionValue = positionQuantity > 0 ? candle.Close * positionQuantity : null;
                rowPositionPnl = rowPositionValue is not null ? rowPositionValue - positionCost : null;
                rowPositionPnlPercent = positionCost > 0 && rowPositionPnl is not null ? rowPositionPnl / positionCost * 100m : null;

                var leg = openLegs[0];
                openLegs.RemoveAt(0);

                var pnl = (candle.Close - leg.EntryPrice) * leg.Quantity;
                var pnlPercent = (candle.Close - leg.EntryPrice) / leg.EntryPrice * 100m;
                trades.Add(new BacktestTrade(
                    leg.EntryIndex,
                    i,
                    leg.EntryTime,
                    candle.Timestamp,
                    leg.EntryPrice,
                    candle.Close,
                    leg.Quantity,
                    pnl,
                    pnlPercent));

                remainingCapital += candle.Close * leg.Quantity;
                action = "exit";
            }

            avgEntry = CalculateAverageEntry(openLegs);
            positionQuantity = openLegs.Sum(l => l.Quantity);
            positionCost = openLegs.Sum(l => l.EntryPrice * l.Quantity);
            var positionValue = positionQuantity > 0 ? candle.Close * positionQuantity : 0m;
            var positionPnl = positionValue - positionCost;
            var positionPnlPercent = positionCost > 0 ? positionPnl / positionCost * 100m : 0m;

            rowAvgEntry ??= avgEntry > 0 ? avgEntry : null;
            rowPositionQuantity ??= positionQuantity > 0 ? positionQuantity : null;
            rowPositionCost ??= positionCost > 0 ? positionCost : null;
            rowPositionValue ??= positionValue > 0 ? positionValue : null;
            rowPositionPnl ??= positionCost > 0 ? positionPnl : null;
            rowPositionPnlPercent ??= positionCost > 0 ? positionPnlPercent : null;

            var item = new BacktestCandleAnalysis(
                i,
                candle.Timestamp,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                currentValues,
                shouldEnter,
                shouldExit,
                rowPositionQuantity > 0,
                action,
                rowAvgEntry,
                rowPositionQuantity,
                rowPositionCost,
                rowPositionValue,
                rowPositionPnl,
                rowPositionPnlPercent);
            analysis.Add(item);
            onAnalysis?.Invoke(item);
        }

        if (openLegs.Count > 0)
        {
            var lastIdx = candles.Count - 1;
            var lastCandle = candles[lastIdx];
            foreach (var leg in openLegs)
            {
                var pnl = (lastCandle.Close - leg.EntryPrice) * leg.Quantity;
                var pnlPercent = (lastCandle.Close - leg.EntryPrice) / leg.EntryPrice * 100m;
                trades.Add(new BacktestTrade(
                    leg.EntryIndex,
                    lastIdx,
                    leg.EntryTime,
                    lastCandle.Timestamp,
                    leg.EntryPrice,
                    lastCandle.Close,
                    leg.Quantity,
                    pnl,
                    pnlPercent));
            }
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
                DetailJson = $"{{\"message\":\"无交易产生\"}}"
            };

        var wins = trades.Count(t => t.Pnl > 0);
        var winRate = (decimal)wins / trades.Count * 100;

        var totalReturn = trades.Sum(t => t.PnlPercent);
        var totalDays = (candles[^1].Timestamp - candles[0].Timestamp).TotalDays;
        var annualizedReturn = 0m;
        if (totalDays > 0)
        {
            var baseVal = 1 + (double)Math.Max(-50, Math.Min(9999, totalReturn)) / 100;
            if (baseVal > 0)
            {
                annualizedReturn = (decimal)Math.Max(-9999, Math.Min(9999, Math.Pow(baseVal, 365.0 / totalDays) - 1)) * 100;
            }
        }

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
            ? (decimal)Math.Sqrt(Math.Max(0, Math.Min(1e10, trades.Average(t => Math.Pow((double)Math.Max(-9999, Math.Min(9999, t.PnlPercent - avgReturn)), 2)))))
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
            DetailJson = detailJson
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
            DetailJson = $"{{\"message\":\"{reason}\"}}"
        };

    private static decimal CalculateAverageEntry(IReadOnlyList<OpenLeg> openLegs)
    {
        var totalQuantity = openLegs.Sum(l => l.Quantity);
        if (totalQuantity <= 0)
            return 0m;

        var totalCost = openLegs.Sum(l => l.EntryPrice * l.Quantity);
        return totalCost / totalQuantity;
    }

    private static bool CheckWindowedVolatility(
        IReadOnlyList<Candle> primaryCandles,
        int currentIndex,
        string? timeframe,
        decimal thresholdPercent,
        decimal currentPrice)
    {
        var minsPerCandle = timeframe switch
        {
            "1m" => 1, "5m" => 5, "15m" => 15, "30m" => 30,
            "1h" => 60, "4h" => 240, "1d" => 1440,
            _ => 15
        };

        var (fiveMinWin, fiveMinLookback) = WindowParams(minsPerCandle, 5);
        if (CheckVolatilityWindow(primaryCandles, currentIndex, fiveMinWin, fiveMinLookback, thresholdPercent, currentPrice))
            return true;

        var (fifteenMinWin, fifteenMinLookback) = WindowParams(minsPerCandle, 15);
        if (CheckVolatilityWindow(primaryCandles, currentIndex, fifteenMinWin, fifteenMinLookback, thresholdPercent, currentPrice))
            return true;

        return false;
    }

    private static (int windowSize, int lookbackCandles) WindowParams(int minsPerCandle, int targetMinutes)
    {
        if (minsPerCandle >= targetMinutes)
            return (1, 30);
        var windowSize = targetMinutes / minsPerCandle;
        var lookbackCandles = targetMinutes * 30 / minsPerCandle;
        return (windowSize, lookbackCandles);
    }

    private static bool CheckVolatilityWindow(
        IReadOnlyList<Candle> primaryCandles,
        int currentIndex,
        int windowSize,
        int lookbackCandles,
        decimal thresholdPercent,
        decimal currentPrice)
    {
        var start = Math.Max(0, currentIndex - lookbackCandles + 1);
        if (start >= currentIndex)
            return false;

        for (var j = start; j + windowSize - 1 <= currentIndex; j += windowSize)
        {
            var winOpen = primaryCandles[j].Open;
            var winHigh = primaryCandles[j].High;
            var winLow = primaryCandles[j].Low;
            for (var k = j + 1; k < j + windowSize; k++)
            {
                if (primaryCandles[k].High > winHigh) winHigh = primaryCandles[k].High;
                if (primaryCandles[k].Low < winLow) winLow = primaryCandles[k].Low;
            }

            if (winOpen <= 0)
                continue;

            var rangePct = (winHigh - winLow) / winOpen * 100m;
            if (rangePct >= thresholdPercent)
            {
                var midPrice = (winHigh + winLow) / 2m;
                if (currentPrice < midPrice)
                    return true;
            }
        }

        return false;
    }

    private sealed record OpenLeg(int EntryIndex, DateTime EntryTime, decimal EntryPrice, decimal Quantity);
}
