using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading.Engine;

namespace TradeX.Trading.Backtest;

public class BacktestEngine(IIndicatorRegistry indicators, IStrategyDecisionEngine decisionEngine)
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

        // 每次 Run 用唯一作用域键，隔离 MinInterval 冷却状态——回测 Worker 中 ITriggerTracker
        // 为进程级单例，固定键会让不同回测任务（甚至同名规则）的冷却互相串扰。
        var scopeKey = $"backtest:{Guid.NewGuid():N}";

        // FIFO 持仓队列：每次加仓（buy）追加一笔 lot，减仓（reduceOneLot）平掉最早一笔，
        // 全平（sellAll）逐笔平掉，与实盘"按持仓逐笔下单/平仓"行为对齐，支持网格/金字塔加仓。
        var lots = new List<Lot>();
        // 现金 + 持仓市值 = 账户权益。现金随平仓回笼，全仓模式下次入场即用最新现金 → 自然复利。
        var cash = initialCapital;
        // 每根 K 线的账户权益序列，用于回撤 / Sharpe（基于策略资金曲线而非标的价格）。
        List<decimal> equityCurve = [];

        // 平掉一笔 lot：记录 trade（含两腿手续费的已实现盈亏），返回回笼现金。
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

            // 统一决策：通过 IStrategyDecisionEngine 评估。回测作用域键固定 "backtest"，
            // 评估时间用当前 K 线时间，使 MinInterval 约束按模拟时间生效。
            var decision = decisionEngine.Decide(new StrategyDecisionInput(
                ExecutionRule: strategy.ExecutionRule,
                IndicatorValues: currentValues,
                PreviousIndicatorValues: previousValues,
                CurrentPrice: price,
                AverageEntryPrice: avgEntry,
                QuantityHeld: quantityHeld,
                LotCount: lots.Count,
                ScopeKey: scopeKey,
                EvaluationTime: ohlc.Timestamp));

            var didBuy = false;
            var didSell = false;

            switch (decision.Action)
            {
                case StrategyAction.EnterMarket when price > 0:
                    // 入场金额优先级：入参 positionSize > 规则定义的 Size > 全仓（复利）
                    var capitalToUse = positionSize.HasValue
                        ? Math.Min(positionSize.Value, cash)
                        : decision.QuoteSize > 0
                            ? Math.Min(decision.QuoteSize, cash)
                            : cash;
                    if (capitalToUse > 0)
                    {
                        // 含手续费入场：capitalToUse 同时覆盖 base 成本与买入手续费（feeRate=0 时与原逻辑等价）
                        var qty = capitalToUse / (price * (1 + feeRate));
                        if (qty > 0)
                        {
                            var fee = qty * price * feeRate;
                            lots.Add(new Lot(price, qty, fee, i, ohlc.Timestamp));
                            cash -= qty * price + fee;
                            didBuy = true;
                        }
                    }
                    break;

                case StrategyAction.Reduce when lots.Count > 0:
                    // QuoteSize>0 且价格可用：自最早 lot 累计名义价值平仓直到达到目标金额（含跨过阈值的那笔）；
                    // 否则（金额无效或价格不可用）只平最早一笔，避免累计名义价值恒为 0 误平全部。
                    if (decision.QuoteSize <= 0m || price <= 0m)
                    {
                        cash += CloseLot(lots[0], i, ohlc);
                        lots.RemoveAt(0);
                    }
                    else
                    {
                        var reduced = 0m;
                        while (lots.Count > 0 && reduced < decision.QuoteSize)
                        {
                            reduced += lots[0].Quantity * price;
                            cash += CloseLot(lots[0], i, ohlc);
                            lots.RemoveAt(0);
                        }
                    }
                    didSell = true;
                    break;

                case StrategyAction.ExitAll when lots.Count > 0:
                    foreach (var lot in lots)
                        cash += CloseLot(lot, i, ohlc);
                    lots.Clear();
                    didSell = true;
                    break;
            }

            // 末根强制平掉全部剩余持仓，保证交易账目完整（与原引擎"末根收盘平仓"一致）
            if (i == prices.Length - 1 && lots.Count > 0)
            {
                foreach (var lot in lots)
                    cash += CloseLot(lot, i, ohlc);
                lots.Clear();
                didSell = true;
            }

            // 处理后聚合持仓，用于本根分析行与权益
            var qtyAfter = lots.Sum(l => l.Quantity);
            var avgAfter = qtyAfter > 0 ? lots.Sum(l => l.EntryPrice * l.Quantity) / qtyAfter : 0m;
            var action = didBuy ? "enter" : didSell ? "exit" : "none";

            analysis.Add(new BacktestKlineAnalysis(
                i, ohlc.Timestamp, ohlc.Open, ohlc.High, ohlc.Low, ohlc.Close, ohlc.Volume,
                currentValues, didBuy, didSell, qtyAfter > 0, action,
                avgAfter > 0 ? avgAfter : null,
                qtyAfter > 0 ? qtyAfter : null,
                qtyAfter > 0 ? avgAfter * qtyAfter : null,
                qtyAfter > 0 ? price * qtyAfter : null,
                qtyAfter > 0 ? (price - avgAfter) * qtyAfter : null,
                avgAfter > 0 ? (price - avgAfter) / avgAfter * 100m : null));

            // 账户权益 = 现金 + 当前持仓市值
            equityCurve.Add(cash + qtyAfter * price);

            onAnalysis?.Invoke(analysis[^1]);
        }

        var finalEquity = equityCurve.Count > 0 ? equityCurve[^1] : initialCapital;
        var result = CalculateMetrics(trades, klines, equityCurve, initialCapital, finalEquity, timeframe);
        return (result, trades, analysis);
    }

    /// <summary>回测持仓中的一笔（FIFO 队列元素）。</summary>
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
        // 约定无风险利率 rf = 0，故超额收益 ≈ 逐根平均收益 mean。
        // 在加密市场短周期回测下 rf 影响可忽略；若需精确可在此减去按周期折算的 rf。
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
