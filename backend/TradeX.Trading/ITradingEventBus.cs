namespace TradeX.Trading;

public interface ITradingEventBus
{
    Task PositionUpdatedAsync(Guid traderId, Guid positionId, Guid exchangeId, Guid strategyId,
        string symbolId, decimal quantity, decimal entryPrice, decimal unrealizedPnl,
        decimal realizedPnl, string status, DateTime updatedAtUtc, CancellationToken ct = default);

    Task OrderPlacedAsync(Guid traderId, Guid orderId, Guid exchangeId, Guid? strategyId,
        string symbolId, string side, string type, string status,
        decimal quantity, DateTime placedAtUtc, CancellationToken ct = default);

    Task BindingStatusChangedAsync(Guid traderId, Guid strategyId, string oldStatus,
        string newStatus, string? reason, CancellationToken ct = default);

    Task RiskAlertAsync(Guid traderId, string level, string category, Guid? strategyId,
        string message, CancellationToken ct = default);

    Task DashboardSummaryAsync(Guid traderId, decimal totalPnl, int totalPositions,
        int activeStrategies, decimal dailyPnl, decimal winRate,
        DateTime lastUpdateAtUtc, CancellationToken ct = default);

    Task ExchangeConnectionChangedAsync(Guid traderId, Guid exchangeId, string oldStatus,
        string newStatus, string? errorMessage, CancellationToken ct = default);
}
