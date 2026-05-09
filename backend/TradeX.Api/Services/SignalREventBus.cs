using Microsoft.AspNetCore.SignalR;
using TradeX.Api.Hubs;
using TradeX.Trading;

namespace TradeX.Api.Services;

public class SignalREventBus(IHubContext<TradingHub> hubContext) : ITradingEventBus
{
    public async Task PositionUpdatedAsync(Guid traderId, Guid positionId, Guid exchangeId, Guid strategyId,
        string Pair, decimal quantity, decimal entryPrice, decimal unrealizedPnl,
        decimal realizedPnl, string status, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        await hubContext.Clients.Group($"trader_{traderId}")
            .SendAsync(TradingHub.PositionUpdated, new PositionUpdatedEvent(
                positionId, traderId, exchangeId, strategyId, Pair, quantity,
                entryPrice, unrealizedPnl, realizedPnl, status, updatedAtUtc), ct);
    }

    public async Task OrderPlacedAsync(Guid traderId, Guid orderId, Guid exchangeId, Guid? strategyId,
        string Pair, string side, string type, string status,
        decimal quantity, DateTime placedAtUtc, CancellationToken ct = default)
    {
        await hubContext.Clients.Group($"trader_{traderId}")
            .SendAsync(TradingHub.OrderPlaced, new OrderPlacedEvent(
                orderId, traderId, exchangeId, strategyId, Pair,
                side, type, status, quantity, placedAtUtc), ct);
    }

    public async Task BindingStatusChangedAsync(Guid traderId, Guid strategyId, string oldStatus,
        string newStatus, string? reason, CancellationToken ct = default)
    {
        await hubContext.Clients.Group($"trader_{traderId}")
            .SendAsync(TradingHub.BindingStatusChanged, new BindingStatusChangedEvent(
                strategyId, traderId, oldStatus, newStatus, reason, DateTime.UtcNow), ct);
    }

    public async Task RiskAlertAsync(Guid traderId, string level, string category, Guid? strategyId,
        string message, CancellationToken ct = default)
    {
        await hubContext.Clients.Group($"trader_{traderId}")
            .SendAsync(TradingHub.RiskAlert, new RiskAlertEvent(
                Guid.NewGuid(), level, category, traderId, strategyId, message, DateTime.UtcNow), ct);
    }

    public async Task DashboardSummaryAsync(Guid traderId, decimal totalPnl, int totalPositions,
        int activeStrategies, decimal dailyPnl, decimal winRate,
        DateTime lastUpdateAtUtc, CancellationToken ct = default)
    {
        await hubContext.Clients.Group($"trader_{traderId}")
            .SendAsync(TradingHub.DashboardSummary, new DashboardSummaryEvent(
                totalPnl, totalPositions, activeStrategies, dailyPnl, winRate, lastUpdateAtUtc), ct);
    }

    public async Task ExchangeConnectionChangedAsync(Guid traderId, Guid exchangeId, string oldStatus,
        string newStatus, string? errorMessage, CancellationToken ct = default)
    {
        await hubContext.Clients.Group($"trader_{traderId}")
            .SendAsync(TradingHub.ExchangeConnectionChanged, new ExchangeConnectionChangedEvent(
                exchangeId, traderId, oldStatus, newStatus, errorMessage, DateTime.UtcNow), ct);
    }
}
