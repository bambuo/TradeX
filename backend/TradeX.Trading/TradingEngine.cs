using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class MarketDataCache
{
    public ConcurrentDictionary<string, List<decimal>> PriceHistory { get; } = new();
    public ConcurrentDictionary<string, DateTime> LastTradeTime { get; } = new();
}

public class TradingEngine(
    IServiceScopeFactory scopeFactory,
    MarketDataCache marketData,
    ITradingEventBus eventBus,
    ILogger<TradingEngine> logger) : BackgroundService
{
    private static readonly TimeSpan EvaluationCycle = TimeSpan.FromSeconds(15);

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
        using var cycle = new TradingCycleScope(scopeFactory);

        var activeStrategies = await cycle.StrategyDeploymentRepo.GetAllActiveAsync(ct);
        if (activeStrategies.Count == 0)
            return;

        foreach (var strategy in activeStrategies)
        {
            try
            {
                await EvaluateStrategyAsync(strategy, cycle, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "策略评估异常, StrategyId={StrategyId}, Name={StrategyName}", strategy.Id, strategy.Name);
            }
        }

        await UpdateAllPositionsPnlAsync(cycle.PositionRepo, ct);
        await PushDashboardSummaryAsync(cycle.PositionRepo, cycle.StrategyDeploymentRepo, ct);

        logger.LogInformation("评估周期完成: {StrategyCount} 个活跃策略, 耗时 {Elapsed:F1}s",
            activeStrategies.Count, (DateTime.UtcNow - cycleStart).TotalSeconds);
    }

    private async Task EvaluateStrategyAsync(
        Core.Models.StrategyDeployment strategy,
        TradingCycleScope cycle,
        CancellationToken ct)
    {
        var symbolIds = strategy.SymbolIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (symbolIds.Length == 0)
            return;

        var symbolId = symbolIds[0];

        var prices = GetPriceHistory(symbolId);
        if (prices.Count < 2)
        {
            logger.LogDebug("策略 {StrategyName}: 交易对 {Symbol} 暂无行情数据, 跳过评估", strategy.Name, symbolId);
            return;
        }

        Dictionary<string, decimal> previousValues = new()
        {
            ["RSI"] = cycle.IndicatorService.CalculateRsi(prices[..^1]),
            ["SMA_20"] = cycle.IndicatorService.CalculateSma(prices[..^1], 20),
            ["SMA_50"] = cycle.IndicatorService.CalculateSma(prices[..^1], 50),
        };

        Dictionary<string, decimal> indicatorValues = new()
        {
            ["RSI"] = cycle.IndicatorService.CalculateRsi(prices),
            ["SMA_20"] = cycle.IndicatorService.CalculateSma(prices, 20),
            ["SMA_50"] = cycle.IndicatorService.CalculateSma(prices, 50),
            ["EMA_20"] = cycle.IndicatorService.CalculateEma(prices, 20),
            ["MACD_LINE"] = cycle.IndicatorService.CalculateMacd(prices).MacdLine,
            ["MACD_SIGNAL"] = cycle.IndicatorService.CalculateMacd(prices).SignalLine
        };

        var openPositions = await cycle.PositionRepo.GetByStrategyIdAsync(strategy.Id, ct);
        var hasOpenPosition = openPositions.Any(p => p.Status == PositionStatus.Open);

        if (!hasOpenPosition && !string.IsNullOrWhiteSpace(strategy.EntryConditionJson) && strategy.EntryConditionJson != "{}")
        {
            var shouldEnter = cycle.ConditionEvaluator.Evaluate(strategy.EntryConditionJson, indicatorValues, previousValues);
            if (shouldEnter)
            {
                var riskCheck = await cycle.RiskManager.CheckAsync(strategy.TraderId, strategy.ExchangeId, ct);
                if (!riskCheck.IsAllowed)
                {
                    var msg = string.Join("; ", riskCheck.DeniedReasons);
                    logger.LogWarning("策略 {StrategyName}: 风控拒绝入场, 原因: {Reasons}", strategy.Name, msg);
                    await eventBus.RiskAlertAsync(strategy.TraderId, "Warning", "RiskCheck", strategy.Id, msg, ct);
                    return;
                }

                var symbolRisk = await cycle.RiskManager.CheckSymbolRiskAsync(strategy.TraderId, strategy.ExchangeId, symbolId, ct);
                if (!symbolRisk.IsAllowed)
                {
                    var msg = string.Join("; ", symbolRisk.DeniedReasons);
                    logger.LogWarning("策略 {StrategyName}: 币种风控拒绝, 原因: {Reasons}", strategy.Name, msg);
                    await eventBus.RiskAlertAsync(strategy.TraderId, "Warning", "SymbolRisk", strategy.Id, msg, ct);
                    return;
                }

                var order = new Order
                {
                    TraderId = strategy.TraderId,
                    ExchangeId = strategy.ExchangeId,
                    StrategyId = strategy.Id,
                    SymbolId = symbolId,
                    Side = OrderSide.Buy,
                    Type = OrderType.Market,
                    Quantity = 100,
                    QuoteQuantity = 100
                };

                var result = await cycle.TradeExecutor.ExecuteMarketOrderAsync(order, ct);
                if (result.Success)
                {
                    marketData.LastTradeTime[$"{strategy.TraderId}_{strategy.Id}"] = DateTime.UtcNow;

                    logger.LogInformation("策略 {StrategyName}: 买入成交 {Symbol} {Quantity}",
                        strategy.Name, symbolId, result.FilledQuantity);

                    await eventBus.OrderPlacedAsync(strategy.TraderId, order.Id, order.ExchangeId, order.StrategyId,
                        order.SymbolId, order.Side.ToString(), order.Type.ToString(),
                        order.Status.ToString(), order.Quantity, order.PlacedAtUtc, ct);
                }
                else
                {
                    logger.LogWarning("策略 {StrategyName}: 买入失败 {Symbol}, 原因: {Error}",
                        strategy.Name, symbolId, result.Error);
                }
            }
        }

        if (hasOpenPosition && !string.IsNullOrWhiteSpace(strategy.ExitConditionJson) && strategy.ExitConditionJson != "{}")
        {
            var shouldExit = cycle.ConditionEvaluator.Evaluate(strategy.ExitConditionJson, indicatorValues, previousValues);
            if (shouldExit)
            {
                foreach (var position in openPositions.Where(p => p.Status == PositionStatus.Open))
                {
                    var sellOrder = new Order
                    {
                        TraderId = strategy.TraderId,
                        ExchangeId = strategy.ExchangeId,
                        StrategyId = strategy.Id,
                        PositionId = position.Id,
                        SymbolId = symbolId,
                        Side = OrderSide.Sell,
                        Type = OrderType.Market,
                        Quantity = position.Quantity,
                        QuoteQuantity = position.CurrentPrice * position.Quantity
                    };

                    var result = await cycle.TradeExecutor.ExecuteMarketOrderAsync(sellOrder, ct);
                    if (result.Success)
                    {
                        position.Status = PositionStatus.Closed;
                        position.ClosedAtUtc = DateTime.UtcNow;
                        position.UpdatedAtUtc = DateTime.UtcNow;
                        await cycle.PositionRepo.UpdateAsync(position, ct);

                        logger.LogInformation("策略 {StrategyName}: 卖出平仓 {Symbol} {Quantity}, PnL={PnL}",
                            strategy.Name, symbolId, position.Quantity, position.RealizedPnl);

                        await eventBus.PositionUpdatedAsync(strategy.TraderId, position.Id, position.ExchangeId,
                            position.StrategyId, position.SymbolId, position.Quantity, position.EntryPrice,
                            position.UnrealizedPnl, position.RealizedPnl, position.Status.ToString(),
                            position.UpdatedAtUtc, ct);

                        await eventBus.OrderPlacedAsync(strategy.TraderId, sellOrder.Id, sellOrder.ExchangeId,
                            sellOrder.StrategyId, sellOrder.SymbolId, sellOrder.Side.ToString(),
                            sellOrder.Type.ToString(), sellOrder.Status.ToString(),
                            sellOrder.Quantity, sellOrder.PlacedAtUtc, ct);
                    }
                }
            }
        }
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
                var prices = GetPriceHistory(position.SymbolId);
                if (prices.Count == 0) continue;

                var lastPrice = prices[^1];
                var oldUnrealizedPnl = position.UnrealizedPnl;

                position.CurrentPrice = lastPrice;
                position.UnrealizedPnl = (lastPrice - position.EntryPrice) * position.Quantity;
                position.UpdatedAtUtc = DateTime.UtcNow;

                await positionRepo.UpdateAsync(position, ct);

                if (Math.Abs(position.UnrealizedPnl - oldUnrealizedPnl) > 0.01m)
                {
                    await eventBus.PositionUpdatedAsync(position.TraderId, position.Id, position.ExchangeId,
                        position.StrategyId, position.SymbolId, position.Quantity, position.EntryPrice,
                        position.UnrealizedPnl, position.RealizedPnl, position.Status.ToString(),
                        position.UpdatedAtUtc, ct);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "更新持仓 PnL 时发生异常");
        }
    }

    private async Task PushDashboardSummaryAsync(IPositionRepository positionRepo, IStrategyDeploymentRepository strategyDeploymentRepo, CancellationToken ct)
    {
        try
        {
            var allPositions = await positionRepo.GetAllOpenAsync(ct);
            var activeStrategies = await strategyDeploymentRepo.GetAllActiveAsync(ct);

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

    private List<decimal> GetPriceHistory(string symbolId)
    {
        return marketData.PriceHistory.GetValueOrDefault(symbolId, []);
    }
}
