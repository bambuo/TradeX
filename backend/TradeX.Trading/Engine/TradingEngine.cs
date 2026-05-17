using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Execution;
using TradeX.Trading.Messaging;
using TradeX.Trading.Observability;

namespace TradeX.Trading.Engine;

public class MarketDataCache
{
    public ConcurrentDictionary<string, List<decimal>> PriceHistory { get; } = new();
    public ConcurrentDictionary<string, DateTime> LastTradeTime { get; } = new();
}

public class TradingEngine(
    IServiceScopeFactory scopeFactory,
    MarketDataCache marketData,
    ITradingEventBus eventBus,
    TradeXMetrics metrics,
    Microsoft.Extensions.Options.IOptions<TradeX.Trading.Risk.RiskSettings> riskSettings,
    ILogger<TradingEngine> logger) : BackgroundService
{
    private static readonly TimeSpan EvaluationCycle = TimeSpan.FromSeconds(15);
    private const string VolatilityGridDedupSecondsKey = "risk.volatility_grid_dedup_seconds";
    private const int DefaultVolatilityGridDedupSeconds = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("交易引擎启动, 评估周期: {Cycle}s", EvaluationCycle.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStart = DateTime.UtcNow;
            try
            {
                await ProcessCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "交易引擎周期处理异常");
            }

            var elapsed = DateTime.UtcNow - cycleStart;
            var delay = EvaluationCycle - elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);
            else
                logger.LogWarning("评估周期超时: {Elapsed}s > {Cycle}s", elapsed.TotalSeconds, EvaluationCycle.TotalSeconds);
        }
    }

    private async Task ProcessCycleAsync(CancellationToken ct)
    {
        var cycleStart = DateTime.UtcNow;

        // 阶段 1：读取活跃策略 + 配置（短暂使用一个 scope）
        List<Core.Models.StrategyBinding> activeStrategies;
        TimeSpan volatilityGridDedupWindow;
        using (var initScope = new TradingCycleScope(scopeFactory))
        {
            activeStrategies = await initScope.StrategyBindingRepo.GetAllActiveAsync(ct);
            if (activeStrategies.Count == 0)
                return;
            volatilityGridDedupWindow = await ResolveVolatilityGridDedupWindowAsync(initScope.SystemConfigRepo, ct);
        }

        // 阶段 2：按 trader 分组并行评估
        // 同一 trader 内的策略保持顺序（共享 DI scope = 共享 DbContext + 避免同 trader 风控竞争）；
        // 不同 trader 并行（各自独立 scope/DbContext，无线程安全冲突）。
        var groupedByTrader = activeStrategies.GroupBy(s => s.TraderId).ToList();
        var configured = riskSettings.Value.StrategyEvaluationParallelism;
        var parallelism = configured > 0
            ? Math.Min(configured, groupedByTrader.Count)
            : Math.Min(Environment.ProcessorCount, groupedByTrader.Count);
        parallelism = Math.Max(1, parallelism);

        await Parallel.ForEachAsync(groupedByTrader,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (traderGroup, token) =>
            {
                using var cycle = new TradingCycleScope(scopeFactory);
                foreach (var strategy in traderGroup)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        await EvaluateStrategyAsync(strategy, cycle, volatilityGridDedupWindow, token);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "策略评估异常, TraderId={TraderId}, StrategyId={StrategyId}, Name={StrategyName}",
                            traderGroup.Key, strategy.Id, strategy.Name);
                    }
                }
            });

        // 阶段 3：position PnL 更新 + dashboard 推送（一个独立 scope）
        using (var finalScope = new TradingCycleScope(scopeFactory))
        {
            await UpdateAllPositionsPnlAsync(finalScope.PositionRepo, ct);
            await PushDashboardSummaryAsync(finalScope.PositionRepo, finalScope.StrategyBindingRepo, ct);
        }

        logger.LogInformation("评估周期完成: {StrategyCount} 个策略 / {TraderCount} 个 trader, 并行度 {Parallelism}, 耗时 {Elapsed:F1}s",
            activeStrategies.Count, groupedByTrader.Count, parallelism, (DateTime.UtcNow - cycleStart).TotalSeconds);
    }

    private async Task EvaluateStrategyAsync(
        Core.Models.StrategyBinding strategy,
        TradingCycleScope cycle,
        TimeSpan volatilityGridDedupWindow,
        CancellationToken ct)
    {
        var pairs = strategy.Pairs.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (pairs.Length == 0)
            return;

        var pair = pairs[0];

        var prices = GetPriceHistory(pair);
        if (prices.Count < 2)
        {
            logger.LogDebug("策略 {StrategyName}: 交易对 {Pair} 暂无行情数据, 跳过评估", strategy.Name, pair);
            return;
        }

        var currentPrice = prices[^1];
        var previousPrice = prices[^2];
        var rangePct = previousPrice > 0
            ? Math.Abs((currentPrice - previousPrice) / previousPrice) * 100m
            : 0m;

        Dictionary<string, decimal> previousValues = new()
        {
            ["RSI"] = cycle.IndicatorService.CalculateRsi(prices[..^1]),
            ["SMA_20"] = cycle.IndicatorService.CalculateSma(prices[..^1], 20),
            ["SMA_50"] = cycle.IndicatorService.CalculateSma(prices[..^1], 50),
            ["RANGE_PCT"] = rangePct,
        };

        Dictionary<string, decimal> indicatorValues = new()
        {
            ["RSI"] = cycle.IndicatorService.CalculateRsi(prices),
            ["SMA_20"] = cycle.IndicatorService.CalculateSma(prices, 20),
            ["SMA_50"] = cycle.IndicatorService.CalculateSma(prices, 50),
            ["EMA_20"] = cycle.IndicatorService.CalculateEma(prices, 20),
            ["MACD_LINE"] = cycle.IndicatorService.CalculateMacd(prices).MacdLine,
            ["MACD_SIGNAL"] = cycle.IndicatorService.CalculateMacd(prices).SignalLine,
            ["RANGE_PCT"] = rangePct
        };

        var openPositions = await cycle.PositionRepo.GetByStrategyIdAsync(strategy.Id, ct);
        var strategyTemplate = strategy.StrategyId != Guid.Empty
            ? await cycle.StrategyRepo.GetByIdAsync(strategy.StrategyId, ct)
            : null;
        var entryConditionJson = strategyTemplate?.EntryCondition ?? "{}";
        var exitConditionJson = strategyTemplate?.ExitCondition ?? "{}";
        var executionRuleJson = strategyTemplate?.ExecutionRule ?? "{}";
        var openPairPositions = openPositions
            .Where(p => p.Status == PositionStatus.Open && p.Pair.Equals(pair, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.OpenedAtUtc)
            .ToList();
        var hasOpenPosition = openPairPositions.Count > 0;

        var volatilityRule = VolatilityGridExecutionRuleParser.TryParse(executionRuleJson, logger);
        var hasEntryCondition = !string.IsNullOrWhiteSpace(entryConditionJson) && entryConditionJson != "{}";
        var hasExitCondition = !string.IsNullOrWhiteSpace(exitConditionJson) && exitConditionJson != "{}";
        var shouldEvaluateEntry = volatilityRule is not null || (!hasOpenPosition && hasEntryCondition);

        if (shouldEvaluateEntry)
        {
            var shouldEnter = volatilityRule is null
                ? cycle.ConditionEvaluator.Evaluate(entryConditionJson, indicatorValues, previousValues)
                : ShouldEnterVolatilityGrid(
                    volatilityRule,
                    hasOpenPosition,
                    openPairPositions,
                    currentPrice,
                    HasRecentPriceMoveBelowMid(prices, volatilityRule.EntryVolatilityPercent, currentPrice));
            if (shouldEnter)
            {
                if (volatilityRule is not null && !CanExecuteVolatilityGridOrder(strategy.TraderId, strategy.ExchangeId, pair, OrderSide.Buy, volatilityGridDedupWindow))
                {
                    logger.LogDebug("策略 {StrategyName}: 命中去重窗口，跳过重复买入, Pair={Pair}", strategy.Name, pair);
                    return;
                }

                var canPyramid = volatilityRule is null
                    || !hasOpenPosition
                    || openPairPositions.Count < volatilityRule.MaxPyramidingLevels + 1;
                if (!canPyramid)
                {
                    logger.LogDebug("策略 {StrategyName}: 已达到最大加仓次数, Pair={Pair}, Max={Max}",
                        strategy.Name, pair, volatilityRule!.MaxPyramidingLevels);
                    return;
                }

                if (volatilityRule is not null)
                {
                    var totalPositionValue = openPairPositions.Sum(p => p.CurrentPrice * p.Quantity) + volatilityRule.BasePositionSize;
                    if (totalPositionValue > volatilityRule.MaxPositionSize)
                    {
                        logger.LogDebug("策略 {StrategyName}: 仓位价值 {Value} 超过上限 {Max}, Pair={Pair}",
                            strategy.Name, totalPositionValue, volatilityRule.MaxPositionSize, pair);
                        return;
                    }
                }

                var riskCheck = await cycle.RiskManager.CheckAsync(strategy.TraderId, strategy.ExchangeId, ct);
                if (!riskCheck.IsAllowed)
                {
                    var msg = string.Join("; ", riskCheck.DeniedReasons);
                    logger.LogWarning("策略 {StrategyName}: 风控拒绝入场, 原因: {Reasons}", strategy.Name, msg);
                    metrics.RiskDenials.Add(1, new KeyValuePair<string, object?>("scope", "portfolio"));
                    await eventBus.RiskAlertAsync(strategy.TraderId, "Warning", "RiskCheck", strategy.Id, msg, ct);
                    return;
                }

                var plannedNotional = volatilityRule?.BasePositionSize ?? 100m;
                var pairRisk = await cycle.RiskManager.CheckPairRiskAsync(strategy.TraderId, strategy.ExchangeId, pair, plannedNotional, ct);
                if (!pairRisk.IsAllowed)
                {
                    var msg = string.Join("; ", pairRisk.DeniedReasons);
                    logger.LogWarning("策略 {StrategyName}: 币种风控拒绝, 原因: {Reasons}", strategy.Name, msg);
                    metrics.RiskDenials.Add(1, new KeyValuePair<string, object?>("scope", "pair"));
                    await eventBus.RiskAlertAsync(strategy.TraderId, "Warning", "PairRisk", strategy.Id, msg, ct);
                    return;
                }

                var order = new Order
                {
                    TraderId = strategy.TraderId,
                    ExchangeId = strategy.ExchangeId,
                    StrategyId = strategy.Id,
                    Pair = pair,
                    Side = OrderSide.Buy,
                    Type = OrderType.Market,
                    Quantity = volatilityRule?.BasePositionSize ?? 100,
                    QuoteQuantity = volatilityRule?.BasePositionSize ?? 100
                };

                var result = await cycle.TradeExecutor.ExecuteMarketOrderAsync(order, ct);
                if (result.Success)
                {
                    if (volatilityRule is not null)
                        MarkVolatilityGridOrderExecuted(strategy.TraderId, strategy.ExchangeId, pair, OrderSide.Buy);

                    marketData.LastTradeTime[$"{strategy.TraderId}_{strategy.Id}"] = DateTime.UtcNow;

                    logger.LogInformation("策略 {StrategyName}: 买入成交 {Pair} {Quantity}",
                        strategy.Name, pair, result.FilledQuantity);

                    metrics.OrdersPlaced.Add(1,
                        new KeyValuePair<string, object?>("side", "buy"),
                        new KeyValuePair<string, object?>("status", order.Status.ToString()));

                    await eventBus.OrderPlacedAsync(strategy.TraderId, order.Id, order.ExchangeId, order.StrategyId,
                        order.Pair, order.Side.ToString(), order.Type.ToString(),
                        order.Status.ToString(), order.Quantity, order.PlacedAtUtc, ct);
                }
                else
                {
                    logger.LogWarning("策略 {StrategyName}: 买入失败 {Pair}, 原因: {Error}",
                        strategy.Name, pair, result.Error);
                    metrics.OrdersRejected.Add(1,
                        new KeyValuePair<string, object?>("side", "buy"),
                        new KeyValuePair<string, object?>("reason", result.Error ?? "unknown"));
                }
            }
        }

        if (hasOpenPosition && (volatilityRule is not null || hasExitCondition))
        {
            var shouldExit = volatilityRule is null
                ? cycle.ConditionEvaluator.Evaluate(exitConditionJson, indicatorValues, previousValues)
                : ShouldExitVolatilityGrid(volatilityRule, openPairPositions, currentPrice);
            if (shouldExit)
            {
                if (volatilityRule is not null && !CanExecuteVolatilityGridOrder(strategy.TraderId, strategy.ExchangeId, pair, OrderSide.Sell, volatilityGridDedupWindow))
                {
                    logger.LogDebug("策略 {StrategyName}: 命中去重窗口，跳过重复卖出, Pair={Pair}", strategy.Name, pair);
                    return;
                }

                var positionsToClose = volatilityRule is null
                    ? openPairPositions
                    : openPairPositions.Take(1).ToList();

                foreach (var position in positionsToClose)
                {
                    var sellOrder = new Order
                    {
                        TraderId = strategy.TraderId,
                        ExchangeId = strategy.ExchangeId,
                        StrategyId = strategy.Id,
                        PositionId = position.Id,
                        Pair = pair,
                        Side = OrderSide.Sell,
                        Type = OrderType.Market,
                        Quantity = position.Quantity,
                        QuoteQuantity = position.CurrentPrice * position.Quantity
                    };

                    var result = await cycle.TradeExecutor.ExecuteMarketOrderAsync(sellOrder, ct);
                    if (result.Success)
                    {
                        if (volatilityRule is not null)
                            MarkVolatilityGridOrderExecuted(strategy.TraderId, strategy.ExchangeId, pair, OrderSide.Sell);

                        position.Status = PositionStatus.Closed;
                        position.ClosedAtUtc = DateTime.UtcNow;
                        position.UpdatedAt = DateTime.UtcNow;
                        await cycle.PositionRepo.UpdateAsync(position, ct);

                        logger.LogInformation("策略 {StrategyName}: 卖出平仓 {Pair} {Quantity}, PnL={PnL}",
                            strategy.Name, pair, position.Quantity, position.RealizedPnl);

                        await eventBus.PositionUpdatedAsync(strategy.TraderId, position.Id, position.ExchangeId,
                            position.StrategyId, position.Pair, position.Quantity, position.EntryPrice,
                            position.UnrealizedPnl, position.RealizedPnl, position.Status.ToString(),
                            position.UpdatedAt, ct);

                        await eventBus.OrderPlacedAsync(strategy.TraderId, sellOrder.Id, sellOrder.ExchangeId,
                            sellOrder.StrategyId, sellOrder.Pair, sellOrder.Side.ToString(),
                            sellOrder.Type.ToString(), sellOrder.Status.ToString(),
                            sellOrder.Quantity, sellOrder.PlacedAtUtc, ct);

                        metrics.OrdersPlaced.Add(1,
                            new KeyValuePair<string, object?>("side", "sell"),
                            new KeyValuePair<string, object?>("status", sellOrder.Status.ToString()));
                    }
                    else
                    {
                        metrics.OrdersRejected.Add(1,
                            new KeyValuePair<string, object?>("side", "sell"),
                            new KeyValuePair<string, object?>("reason", result.Error ?? "unknown"));
                    }
                }
            }
        }
    }

    private static bool ShouldEnterVolatilityGrid(
        VolatilityGridExecutionRule rule,
        bool hasOpenPosition,
        IReadOnlyList<Position> openPairPositions,
        decimal currentPrice,
        bool firstEntrySignal)
    {
        if (!hasOpenPosition)
            return firstEntrySignal;

        var avgEntry = CalculateAverageEntry(openPairPositions);
        if (avgEntry <= 0)
            return false;

        return currentPrice <= avgEntry * (1 - rule.RebalancePercent / 100m);
    }

    private static bool ShouldExitVolatilityGrid(
        VolatilityGridExecutionRule rule,
        IReadOnlyList<Position> openPairPositions,
        decimal currentPrice)
    {
        if (openPairPositions.Count == 0)
            return false;

        var avgEntry = CalculateAverageEntry(openPairPositions);
        if (avgEntry <= 0)
            return false;

        return currentPrice >= avgEntry * (1 + rule.RebalancePercent / 100m);
    }

    private static decimal CalculateAverageEntry(IReadOnlyList<Position> openPairPositions)
    {
        var totalQuantity = openPairPositions.Sum(p => p.Quantity);
        if (totalQuantity <= 0)
            return 0m;

        var totalCost = openPairPositions.Sum(p => p.EntryPrice * p.Quantity);
        return totalCost / totalQuantity;
    }

    private static async Task<TimeSpan> ResolveVolatilityGridDedupWindowAsync(ISystemConfigRepository configRepo, CancellationToken ct)
    {
        var item = await configRepo.GetByKeyAsync(VolatilityGridDedupSecondsKey, ct);
        if (item is null || !int.TryParse(item.Value, out var seconds))
            return TimeSpan.FromSeconds(DefaultVolatilityGridDedupSeconds);

        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600));
    }

    private bool CanExecuteVolatilityGridOrder(Guid traderId, Guid exchangeId, string pair, OrderSide side, TimeSpan dedupWindow)
    {
        var dedupKey = BuildVolatilityGridDedupKey(traderId, exchangeId, pair, side);
        if (!marketData.LastTradeTime.TryGetValue(dedupKey, out var lastTime))
            return true;

        return DateTime.UtcNow - lastTime >= dedupWindow;
    }

    private void MarkVolatilityGridOrderExecuted(Guid traderId, Guid exchangeId, string pair, OrderSide side)
    {
        var dedupKey = BuildVolatilityGridDedupKey(traderId, exchangeId, pair, side);
        marketData.LastTradeTime[dedupKey] = DateTime.UtcNow;
    }

    private static string BuildVolatilityGridDedupKey(Guid traderId, Guid exchangeId, string pair, OrderSide side)
        => $"vg:{traderId}:{exchangeId}:{pair.ToUpperInvariant()}:{side}";

    private static bool HasRecentPriceMoveBelowMid(IReadOnlyList<decimal> prices, decimal thresholdPercent, decimal currentPrice)
    {
        var lookbackStart = Math.Max(0, prices.Count - 1801);
        if (lookbackStart >= prices.Count)
            return false;

        // Ensure at least ~30 minutes of data (120 ticks at 15s) to avoid cold start false triggers
        if (prices.Count - lookbackStart < 120)
            return false;

        var minPrice = prices[lookbackStart];
        var maxPrice = prices[lookbackStart];

        for (var j = lookbackStart + 1; j < prices.Count; j++)
        {
            var p = prices[j];
            if (p < minPrice) minPrice = p;
            if (p > maxPrice) maxPrice = p;
        }

        if (minPrice <= 0)
            return false;

        var rangePct = (maxPrice - minPrice) / minPrice * 100m;
        if (rangePct < thresholdPercent)
            return false;

        var midPrice = (maxPrice + minPrice) / 2m;
        return currentPrice < midPrice;
    }

    private async Task UpdateAllPositionsPnlAsync(IPositionRepository positionRepo, CancellationToken ct)
    {
        try
        {
            var allOpenPositions = await positionRepo.GetAllOpenAsync(ct);
            if (allOpenPositions.Count == 0)
                return;

            foreach (var position in allOpenPositions)
            {
                var prices = GetPriceHistory(position.Pair);
                if (prices.Count == 0) continue;

                var lastPrice = prices[^1];
                var oldUnrealizedPnl = position.UnrealizedPnl;

                position.CurrentPrice = lastPrice;
                position.UnrealizedPnl = (lastPrice - position.EntryPrice) * position.Quantity;
                position.UpdatedAt = DateTime.UtcNow;

                await positionRepo.UpdateAsync(position, ct);

                if (Math.Abs(position.UnrealizedPnl - oldUnrealizedPnl) > 0.01m)
                {
                    await eventBus.PositionUpdatedAsync(position.TraderId, position.Id, position.ExchangeId,
                        position.StrategyId, position.Pair, position.Quantity, position.EntryPrice,
                        position.UnrealizedPnl, position.RealizedPnl, position.Status.ToString(),
                        position.UpdatedAt, ct);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "更新持仓 PnL 时发生异常");
        }
    }

    private async Task PushDashboardSummaryAsync(IPositionRepository positionRepo, IStrategyBindingRepository strategyBindingRepo, CancellationToken ct)
    {
        try
        {
            var allPositions = await positionRepo.GetAllOpenAsync(ct);
            var activeStrategies = await strategyBindingRepo.GetAllActiveAsync(ct);

            var traderGroups = allPositions.GroupBy(p => p.TraderId);
            foreach (var group in traderGroups)
            {
                var totalPnl = group.Sum(p => p.UnrealizedPnl + p.RealizedPnl);
                var totalPositions = group.Count();
                var activeCount = activeStrategies.Count(s => s.TraderId == group.Key);
                var wins = group.Count(p => p.RealizedPnl > 0);
                var winRate = totalPositions > 0 ? (decimal)wins / totalPositions * 100 : 0;

                await eventBus.DashboardSummaryAsync(group.Key,
                    totalPnl, totalPositions, activeCount, 0, winRate,
                    DateTime.UtcNow, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "推送 Dashboard 摘要时发生异常");
        }
    }

    private List<decimal> GetPriceHistory(string pair)
    {
        return marketData.PriceHistory.GetValueOrDefault(pair, []);
    }
}
