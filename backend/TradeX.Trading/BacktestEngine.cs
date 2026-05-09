using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class BacktestEngine
{
    public (BacktestResult Result, List<BacktestTrade> Trades, List<BacktestKlineAnalysis> Analysis) Run(
        Strategy strategy,
        string pair,
        IReadOnlyList<Candle> klines,
        decimal initialCapital = 1000m,
        decimal? positionSize = null,
        Action<BacktestKlineAnalysis>? onAnalysis = null,
        string? timeframe = null)
    {
        if (klines.Count < 50)
            return (CreateEmptyResult("数据不足，至少需要 50 根 K 线"), [], []);

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
            var window = prices[..(i + 1)];
            var prevWindow = prices[..i];
            var volWindow = volumes[..(i + 1)];

            Dictionary<string, decimal> currentValues = new()
            {
                ["RSI"] = CalculateRsi(window),
                ["SMA_20"] = CalculateSma(window, 20),
                ["SMA_50"] = CalculateSma(window, 50),
                ["EMA_20"] = CalculateEma(window, 20),
                ["MACD_LINE"] = CalculateMacd(window).MacdLine,
                ["MACD_SIGNAL"] = CalculateMacd(window).SignalLine,
                ["BB_UPPER"] = CalculateBollingerBands(window).UpperBand,
                ["BB_LOWER"] = CalculateBollingerBands(window).LowerBand,
                ["OBV"] = CalculateObv(prices[..(i + 1)], volWindow),
                ["VOLUME_SMA"] = CalculateVolumeSma(volWindow),
                ["RANGE_PCT"] = klines[i].Open > 0 ? (klines[i].High - klines[i].Low) / klines[i].Open * 100m : 0m,
            };

            Dictionary<string, decimal> previousValues = new()
            {
                ["RSI"] = CalculateRsi(prevWindow),
                ["SMA_20"] = CalculateSma(prevWindow, 20),
                ["SMA_50"] = CalculateSma(prevWindow, 50),
                ["EMA_20"] = CalculateEma(prevWindow, 20),
                ["MACD_LINE"] = CalculateMacd(prevWindow).MacdLine,
                ["MACD_SIGNAL"] = CalculateMacd(prevWindow).SignalLine,
                ["RANGE_PCT"] = klines[i - 1].Open > 0 ? (klines[i - 1].High - klines[i - 1].Low) / klines[i - 1].Open * 100m : 0m,
            };

            var kline = klines[i];
            var action = "none";

            if (!inPosition)
            {
                var shouldEnter = EvaluateCondition(strategy.EntryCondition, currentValues, previousValues);
                if (shouldEnter)
                {
                    inPosition = true;
                    action = "enter";
                    entryPrice = kline.Close;
                    entryIndex = i;
                    entryQuantity = entryPrice > 0 ? workedCapital / entryPrice : 0m;
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
                var shouldExit = EvaluateCondition(strategy.ExitCondition, currentValues, previousValues);
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
            var qty = workedCapital / entryPrice;
            var pnl = (exitPrice - entryPrice) * qty;
            var pnlPercent = (exitPrice - entryPrice) / entryPrice * 100;
            trades.Add(new BacktestTrade(
                entryIndex, lastIdx,
                klines[entryIndex].Timestamp, klines[lastIdx].Timestamp,
                entryPrice, exitPrice, qty, pnl, pnlPercent));
        }

        var result = CalculateMetrics(trades, klines, prices, analysis);
        return (result, trades, analysis);
    }

    private static BacktestResult CalculateMetrics(
        IReadOnlyList<BacktestTrade> trades,
        IReadOnlyList<Candle> klines,
        decimal[] prices,
        IReadOnlyList<BacktestKlineAnalysis> analysis)
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
                Details = $"{{\"message\":\"无交易产生\"}}"
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

        var avgReturn = trades.Count > 0 ? trades.Average(t => t.PnLPercent) : 0;
        var stdDev = trades.Count > 1
            ? (decimal)Math.Sqrt(Math.Max(0, Math.Min(1e10, trades.Average(t => Math.Pow((double)Math.Max(-9999, Math.Min(9999, t.PnLPercent - avgReturn)), 2)))))
            : 1;
        var sharpe = stdDev > 0 ? avgReturn / stdDev * (decimal)Math.Sqrt(365) : 0;

        var totalWin = trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
        var totalLoss = trades.Where(t => t.PnL <= 0).Sum(t => Math.Abs(t.PnL));
        var profitLossRatio = totalLoss > 0 ? totalWin / totalLoss : totalWin > 0 ? totalWin : 0;

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

    private static bool EvaluateCondition(string conditionJson, Dictionary<string, decimal> currentValues, Dictionary<string, decimal> previousValues)
    {
        // Simple JSON-based condition evaluator for backtest
        if (string.IsNullOrWhiteSpace(conditionJson) || conditionJson == "{}")
            return false;

        try
        {
            var node = JsonSerializer.Deserialize<ConditionNode>(conditionJson);
            return EvaluateNode(node, currentValues, previousValues);
        }
        catch
        {
            return false;
        }
    }

    private static bool EvaluateNode(ConditionNode? node, Dictionary<string, decimal> currentValues, Dictionary<string, decimal> previousValues)
    {
        if (node is null) return false;

        if (node.Operator == "AND")
            return node.Conditions?.All(c => EvaluateNode(c, currentValues, previousValues)) ?? true;

        if (node.Operator == "OR")
            return node.Conditions?.Any(c => EvaluateNode(c, currentValues, previousValues)) ?? false;

        if (node.Operator == "NOT")
            return node.Conditions?.Length == 1 && !EvaluateNode(node.Conditions[0], currentValues, previousValues);

        // Leaf node
        var indicator = node.Indicator ?? "";
        var currentVal = currentValues.GetValueOrDefault(indicator, 0m);
        var prevVal = previousValues.GetValueOrDefault(indicator, 0m);

        var refIndicator = node.Ref;
        decimal compareValue = node.Value;
        if (!string.IsNullOrEmpty(refIndicator))
        {
            var refVal = currentValues.GetValueOrDefault(refIndicator, 1m);
            compareValue = refVal * node.Value;
        }

        return node.Comparison switch
        {
            ">" => currentVal > compareValue,
            "<" => currentVal < compareValue,
            ">=" => currentVal >= compareValue,
            "<=" => currentVal <= compareValue,
            "==" => Math.Abs(currentVal - compareValue) < 0.0001m,
            "CA" => prevVal <= compareValue && currentVal > compareValue,
            "CB" => prevVal >= compareValue && currentVal < compareValue,
            _ => false
        };
    }

    // Indicator calculations (inlined for backtest independence)
    private static decimal CalculateRsi(IReadOnlyList<decimal> prices)
    {
        const int period = 14;
        if (prices.Count < period + 1) return 50m;
        var gains = 0m; var losses = 0m;
        for (var i = prices.Count - period; i < prices.Count; i++)
        {
            var diff = prices[i] - prices[i - 1];
            if (diff > 0) gains += diff; else losses -= diff;
        }
        var avgGain = gains / period; var avgLoss = losses / period;
        if (avgLoss == 0) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - 100m / (1 + rs);
    }

    private static decimal CalculateSma(IReadOnlyList<decimal> prices, int period)
    {
        if (prices.Count < period) return 0m;
        var sum = 0m;
        for (var i = prices.Count - period; i < prices.Count; i++) sum += prices[i];
        return sum / period;
    }

    private static decimal CalculateEma(IReadOnlyList<decimal> prices, int period)
    {
        if (prices.Count < period + 1) return prices[^1];
        var sliced = new List<decimal>(prices.Count - 1);
        for (var i = 0; i < prices.Count - 1; i++) sliced.Add(prices[i]);
        var sma = CalculateSma(sliced, period);
        var multiplier = 2m / (period + 1);
        return (prices[^1] - sma) * multiplier + sma;
    }

    private static (decimal MacdLine, decimal SignalLine) CalculateMacd(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 27) return (0, 0);
        var ema12 = CalculateEma(prices, 12);
        var ema26 = CalculateEma(prices, 26);
        var macdLine = ema12 - ema26;
        var signalPrices = new List<decimal>(9);
        var start = prices.Count - 9;
        for (var i = start; i < prices.Count; i++) signalPrices.Add(prices[i]);
        var signalLine = CalculateEma(signalPrices, 9);
        return (macdLine, signalLine);
    }

    private static (decimal UpperBand, decimal LowerBand) CalculateBollingerBands(IReadOnlyList<decimal> prices)
    {
        const int period = 20;
        if (prices.Count < period) return (0, 0);
        var sma = CalculateSma(prices, period);
        var variance = 0d;
        var count = 0;
        for (var i = prices.Count - period; i < prices.Count; i++, count++)
        {
            var diff = (double)(prices[i] - sma);
            variance += diff * diff;
        }
        variance /= count;
        var std = (decimal)Math.Sqrt(variance);
        return (sma + 2 * std, sma - 2 * std);
    }

    private static decimal CalculateObv(IReadOnlyList<decimal> closes, IReadOnlyList<long> volumes)
    {
        if (closes.Count < 2 || volumes.Count < 2) return 0m;
        var obv = 0m;
        for (var i = 1; i < closes.Count; i++)
        {
            if (closes[i] > closes[i - 1]) obv += volumes[i];
            else if (closes[i] < closes[i - 1]) obv -= volumes[i];
        }
        return obv;
    }

    private static decimal CalculateVolumeSma(IReadOnlyList<long> volumes)
    {
        const int period = 20;
        if (volumes.Count < period) return volumes.Count > 0 ? (decimal)volumes.Average() : 0m;
        return (decimal)volumes.Skip(volumes.Count - period).Take(period).Average();
    }

    private class ConditionNode
    {
        public string? Operator { get; set; }
        public ConditionNode[]? Conditions { get; set; }
        public string? Indicator { get; set; }
        public string? Comparison { get; set; }
        public decimal Value { get; set; }
        public string? Ref { get; set; }
    }
}
