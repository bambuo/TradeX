using Microsoft.Extensions.Logging;

namespace TradeX.Trading.Messaging;

/// <summary>
/// ITradingEventBus 的降级实现 —— 未配置 Redis 时用于 Worker 进程，把事件仅打到日志而不推送前端。
/// 正常路径用 <c>OutboxTradingEventBus</c>（写 outbox）+ <c>OutboxRelayService</c>（XADD 到
/// tradex:events Stream）+ API 端 <c>RedisToSignalRBridge</c>（消费组订阅后桥接 SignalR）。
///
/// 影响：降级期间前端 SignalR 实时事件缺失；订单/持仓真相仍以 DB 为准，前端可刷新/轮询查最新状态。
/// </summary>
public sealed class LoggingEventBus(ILogger<LoggingEventBus> logger) : ITradingEventBus
{
    public Task PositionUpdatedAsync(Guid traderId, Guid positionId, Guid exchangeId, Guid strategyId,
        string Pair, decimal quantity, decimal entryPrice, decimal unrealizedPnl,
        decimal realizedPnl, string status, DateTime updatedAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
    {
        logger.LogDebug("EventBus(Logging) PositionUpdated: TraceId={TraceId}, TraderId={Trader}, Pair={Pair}, Status={Status}, RealizedPnl={Pnl}",
            traceId ?? Guid.Empty, traderId, Pair, status, realizedPnl);
        return Task.CompletedTask;
    }

    public Task OrderPlacedAsync(Guid traderId, Guid orderId, Guid exchangeId, Guid? strategyId,
        string Pair, string side, string type, string status,
        decimal quantity, DateTime placedAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
    {
        logger.LogDebug("EventBus(Logging) OrderPlaced: TraceId={TraceId}, TraderId={Trader}, OrderId={Order}, {Side} {Type} {Pair} qty={Qty} status={Status}",
            traceId ?? Guid.Empty, traderId, orderId, side, type, Pair, quantity, status);
        return Task.CompletedTask;
    }

    public Task BindingStatusChangedAsync(Guid traderId, Guid strategyId, string oldStatus,
        string newStatus, string? reason,
        CancellationToken ct = default, Guid? traceId = null)
    {
        logger.LogDebug("EventBus(Logging) BindingStatusChanged: TraceId={TraceId}, TraderId={Trader}, StrategyId={Strategy}, {Old}→{New}, Reason={Reason}",
            traceId ?? Guid.Empty, traderId, strategyId, oldStatus, newStatus, reason);
        return Task.CompletedTask;
    }

    public Task RiskAlertAsync(Guid traderId, string level, string category, Guid? strategyId,
        string message,
        CancellationToken ct = default, Guid? traceId = null)
    {
        logger.LogWarning("EventBus(Logging) RiskAlert[{Level}/{Category}]: TraceId={TraceId}, TraderId={Trader}, StrategyId={Strategy}, Msg={Msg}",
            level, category, traceId ?? Guid.Empty, traderId, strategyId, message);
        return Task.CompletedTask;
    }

    public Task DashboardSummaryAsync(Guid traderId, decimal totalPnl, int totalPositions,
        int activeStrategies, decimal dailyPnl, decimal winRate,
        DateTime lastUpdateAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
    {
        logger.LogDebug("EventBus(Logging) DashboardSummary: TraceId={TraceId}, TraderId={Trader}, TotalPnl={Total}, DailyPnl={Daily}, Positions={Pos}, Strategies={Strat}",
            traceId ?? Guid.Empty, traderId, totalPnl, dailyPnl, totalPositions, activeStrategies);
        return Task.CompletedTask;
    }

    public Task ExchangeConnectionChangedAsync(Guid traderId, Guid exchangeId, string oldStatus,
        string newStatus, string? errorMessage,
        CancellationToken ct = default, Guid? traceId = null)
    {
        logger.LogInformation("EventBus(Logging) ExchangeConnectionChanged: TraceId={TraceId}, TraderId={Trader}, ExchangeId={Exch}, {Old}→{New}, Err={Err}",
            traceId ?? Guid.Empty, traderId, exchangeId, oldStatus, newStatus, errorMessage);
        return Task.CompletedTask;
    }
}
