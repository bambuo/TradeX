using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading.Rules;

namespace TradeX.Trading.Backtest;

public class BacktestEngine(IIndicatorRegistry indicators, StrategyEvaluator strategyEvaluator)
{
    private const int MaxKlines = 100_000;

    public (BacktestResult Result, List<BacktestTrade> Trades, List<BacktestKlineAnalysis> Analysis) Run(
        Strategy strategy,
        string pair,
        IReadOnlyList<Kline> klines,
        decimal initialCapital = 1000m,
        decimal? positionSize = null,
        Action<BacktestKlineAnalysis>? onAnalysis = null,
        string? timeframe = null,
        CancellationToken ct = default,
        decimal feeRate = 0m)
    {
        if (klines.Count < 50)
            return (CreateEmptyResult("数据不足，至少需要 50 根 K 线", initialCapital), [], []);

        if (klines.Count > MaxKlines)
            return (CreateEmptyResult($"数据量过大，超过 {MaxKlines} 根 K 线上限", initialCapital), [], []);

        List<BacktestTrade> trades = [];
        List<BacktestKlineAnalysis> analysis = [];
        var prices = klines.Select(c => c.Close).ToArray();
        var volumes = klines.Select(c => (long)c.Volume).ToArray();

        // 每次 Run 用唯一作用域键，隔离 MinInterval 冷却状态
        var scopeKey = $"backtest:{Guid.NewGuid():N}";

        // 历史指标快照列表
        List<Dictionary<string, decimal>> historicalSnapshots = [];

        var lots = new List<Lot>();
        var cash = initialCapital;
        List<decimal> equityCurve = [];

        decimal CloseLot(Lot lot, int exitIndex, Kline exitOhlc)
        {
            var exitPrice = exitOhlc.Close;
            var exitFee = lot.Quantity * exitPrice * feeRate;
            var pnl = (exitPrice - lot.EntryPrice) * lot.Quantity - lot.EntryFee - exitFee;
            var costBasis = lot.EntryPrice * lot.Quantity + lot.EntryFee;
            var pnlPercent = costBasis > 0 ? pnl / costBasis * 100 : 0;
            trades.Add(new BacktestTrade(
                lot.EntryIndex, exitIndex,
                lot.EntryTime, exitOhlc.Timestamp,
                lot.EntryPrice, exitPrice, lot.Quantity, pnl, pnlPercent));
            return lot.Quantity * exitPrice - exitFee;
        }

        // 创建回测用的 StrategyBinding
        var binding = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            StrategyId = strategy.Id,
            TraderId = strategy.CreatedBy,
            Pairs = pair,
            Timeframe = timeframe ?? "1h",
            Status = Core.Enums.BindingStatus.Active,
            Name = strategy.Name,
        };

        for (var i = 50; i < prices.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ohlc = klines[i];
            var prevOhlc = klines[i - 1];
            var currentValues = indicators.ComputeAll(new KlineWindow(prices[..(i + 1)], volumes[..(i + 1)], ohlc.Open, ohlc.High, ohlc.Low, ohlc.Close));
            var previousValues = indicators.ComputeAll(new KlineWindow(prices[..i], volumes[..i], prevOhlc.Open, prevOhlc.High, prevOhlc.Low, prevOhlc.Close));

            var price = ohlc.Close;
            var quantityHeld = lots.Sum(l => l.Quantity);
            var avgEntry = quantityHeld > 0 ? lots.Sum(l => l.EntryPrice * l.Quantity) / quantityHeld : 0m;

            historicalSnapshots.Add(new Dictionary<string, decimal>(currentValues));

            // 通过 StrategyEvaluator 评估规则链
            if (strategy.Mode == Core.Enums.StrategyMode.RuleChain)
            {
                strategyEvaluator.EvaluateBindingChain(
                    binding, pair, price, Guid.Empty,
                    klines, ct);
            }

            // 末根强制平掉全部剩余持仓
            if (i == prices.Length - 1 && lots.Count > 0)
            {
                foreach (var lot in lots)
                    cash += CloseLot(lot, i, ohlc);
                lots.Clear();
            }

            var qtyAfter = lots.Sum(l => l.Quantity);
            var avgAfter = qtyAfter > 0 ? lots.Sum(l => l.EntryPrice * l.Quantity) / qtyAfter : 0m;

            analysis.Add(new BacktestKlineAnalysis(
                i, ohlc.Timestamp, ohlc.Open, ohlc.High, ohlc.Low, ohlc.Close, ohlc.Volume,
                currentValues, null, null, qtyAfter > 0, "none",
                avgAfter > 0 ? avgAfter : null,
                qtyAfter > 0 ? qtyAfter : null,
                qtyAfter > 0 ? avgAfter * qtyAfter : null,
                qtyAfter > 0 ? price * qtyAfter : null,
                qtyAfter > 0 ? (price - avgAfter) * qtyAfter : null,
                avgAfter > 0 ? (price - avgAfter) / avgAfter * 100m : null));

            equityCurve.Add(cash + qtyAfter * price);
            onAnalysis?.Invoke(analysis[^1]);
        }

        var finalEquity = equityCurve.Count > 0 ? equityCurve[^1] : initialCapital;
        var result = CalculateMetrics(trades, klines, equityCurve, initialCapital, finalEquity, timeframe);
        return (result, trades, analysis);
    }

    private sealed record Lot(decimal EntryPrice, decimal Quantity, decimal EntryFee, int EntryIndex, DateTime EntryTime);

    private static BacktestResult CalculateMetrics(
        IReadOnlyList<BacktestTrade> trades,
        IReadOnlyList<Kline> klines,
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

        var totalReturn = initialCapital > 0 ? (finalEquity - initialCapital) / initialCapital * 100 : 0;

        var totalDays = (klines[^1].Timestamp - klines[0].Timestamp).TotalDays;
        var annualizedReturn = 0m;
        if (totalDays > 0 && initialCapital > 0 && finalEquity > 0)
        {
            var growth = (double)(finalEquity / initialCapital);
            annualizedReturn = (decimal)Math.Max(-9999, Math.Min(9999, Math.Pow(growth, 365.0 / totalDays) - 1)) * 100;
        }

        var maxDrawdown = ComputeMaxDrawdown(equityCurve);
        var sharpe = ComputeSharpe(equityCurve, timeframe);

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
