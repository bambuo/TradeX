namespace TradeX.Trading.Messaging;

public interface ITradingEventBus
{
    Task PositionUpdatedAsync(Guid traderId, Guid positionId, Guid exchangeId, Guid strategyId,
        string Pair, decimal quantity, decimal entryPrice, decimal unrealizedPnl,
        decimal realizedPnl, string status, DateTime updatedAtUtc,
        CancellationToken ct = default, Guid? traceId = null);

    Task OrderPlacedAsync(Guid traderId, Guid orderId, Guid exchangeId, Guid? strategyId,
        string Pair, string side, string type, string status,
        decimal quantity, DateTime placedAtUtc,
        CancellationToken ct = default, Guid? traceId = null);

    Task BindingStatusChangedAsync(Guid traderId, Guid strategyId, string oldStatus,
        string newStatus, string? reason,
        CancellationToken ct = default, Guid? traceId = null);

    Task RiskAlertAsync(Guid traderId, string level, string category, Guid? strategyId,
        string message,
        CancellationToken ct = default, Guid? traceId = null);

    Task DashboardSummaryAsync(Guid traderId, decimal totalPnl, int totalPositions,
        int activeStrategies, decimal dailyPnl, decimal winRate,
        DateTime lastUpdateAtUtc,
        CancellationToken ct = default, Guid? traceId = null);

    Task ExchangeConnectionChangedAsync(Guid traderId, Guid exchangeId, string oldStatus,
        string newStatus, string? errorMessage,
        CancellationToken ct = default, Guid? traceId = null);
}
