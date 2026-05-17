using Microsoft.Extensions.Logging;

namespace TradeX.Trading.Messaging;

/// <summary>
/// ITradingEventBus 的临时实现 —— 阶段 2 用于 Worker 进程，把事件仅打到日志而不推送给前端。
/// 阶段 3 会替换为 RedisEventBus（Worker 端发布到 Redis 频道，API 端订阅后桥接到 SignalR）。
///
/// 影响：在阶段 2 期间，前端 SignalR 实时事件会暂时缺失；订单/持仓状态的真相仍以 DB 为准，
/// 前端可通过下拉刷新或定时轮询查到最新状态。
/// </summary>
public sealed class LoggingEventBus(ILogger<LoggingEventBus> logger) : ITradingEventBus
{
    public Task PositionUpdatedAsync(Guid traderId, Guid positionId, Guid exchangeId, Guid strategyId,
        string Pair, decimal quantity, decimal entryPrice, decimal unrealizedPnl,
        decimal realizedPnl, string status, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        logger.LogDebug("EventBus(Logging) PositionUpdated: TraderId={Trader}, Pair={Pair}, Status={Status}, RealizedPnl={Pnl}",
            traderId, Pair, status, realizedPnl);
        return Task.CompletedTask;
    }

    public Task OrderPlacedAsync(Guid traderId, Guid orderId, Guid exchangeId, Guid? strategyId,
        string Pair, string side, string type, string status,
        decimal quantity, DateTime placedAtUtc, CancellationToken ct = default)
    {
        logger.LogDebug("EventBus(Logging) OrderPlaced: TraderId={Trader}, OrderId={Order}, {Side} {Type} {Pair} qty={Qty} status={Status}",
            traderId, orderId, side, type, Pair, quantity, status);
        return Task.CompletedTask;
    }

    public Task BindingStatusChangedAsync(Guid traderId, Guid strategyId, string oldStatus,
        string newStatus, string? reason, CancellationToken ct = default)
    {
        logger.LogDebug("EventBus(Logging) BindingStatusChanged: TraderId={Trader}, StrategyId={Strategy}, {Old}→{New}, Reason={Reason}",
            traderId, strategyId, oldStatus, newStatus, reason);
        return Task.CompletedTask;
    }

    public Task RiskAlertAsync(Guid traderId, string level, string category, Guid? strategyId,
        string message, CancellationToken ct = default)
    {
        logger.LogWarning("EventBus(Logging) RiskAlert[{Level}/{Category}]: TraderId={Trader}, StrategyId={Strategy}, Msg={Msg}",
            level, category, traderId, strategyId, message);
        return Task.CompletedTask;
    }

    public Task DashboardSummaryAsync(Guid traderId, decimal totalPnl, int totalPositions,
        int activeStrategies, decimal dailyPnl, decimal winRate,
        DateTime lastUpdateAtUtc, CancellationToken ct = default)
    {
        logger.LogDebug("EventBus(Logging) DashboardSummary: TraderId={Trader}, TotalPnl={Total}, DailyPnl={Daily}, Positions={Pos}, Strategies={Strat}",
            traderId, totalPnl, dailyPnl, totalPositions, activeStrategies);
        return Task.CompletedTask;
    }

    public Task ExchangeConnectionChangedAsync(Guid traderId, Guid exchangeId, string oldStatus,
        string newStatus, string? errorMessage, CancellationToken ct = default)
    {
        logger.LogInformation("EventBus(Logging) ExchangeConnectionChanged: TraderId={Trader}, ExchangeId={Exch}, {Old}→{New}, Err={Err}",
            traderId, exchangeId, oldStatus, newStatus, errorMessage);
        return Task.CompletedTask;
    }
}
