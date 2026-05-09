using Microsoft.AspNetCore.SignalR;

namespace TradeX.Api.Hubs;

[Microsoft.AspNetCore.Authorization.Authorize]
public class TradingHub : Hub
{
    public const string PositionUpdated = "PositionUpdated";
    public const string OrderPlaced = "OrderPlaced";
    public const string BindingStatusChanged = "BindingStatusChanged";
    public const string RiskAlert = "RiskAlert";
    public const string DashboardSummary = "DashboardSummary";
    public const string ExchangeConnectionChanged = "ExchangeConnectionChanged";

    public async Task JoinTraderGroup(string traderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"trader_{traderId}");
    }

    public async Task LeaveTraderGroup(string traderId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trader_{traderId}");
    }
}

public record PositionUpdatedEvent(
    Guid PositionId, Guid TraderId, Guid ExchangeId, Guid StrategyId,
    string SymbolId, decimal Quantity, decimal EntryPrice, decimal UnrealizedPnl,
    decimal RealizedPnl, string Status, DateTime UpdatedAt);

public record OrderPlacedEvent(
    Guid OrderId, Guid TraderId, Guid ExchangeId, Guid? StrategyId,
    string SymbolId, string Side, string Type, string Status,
    decimal Quantity, DateTime PlacedAtUtc);

public record BindingStatusChangedEvent(
    Guid StrategyId, Guid TraderId, string OldStatus, string NewStatus,
    string? Reason, DateTime ChangedAtUtc);

public record RiskAlertEvent(
    Guid AlertId, string Level, string Category, Guid TraderId,
    Guid? StrategyId, string Message, DateTime TriggeredAtUtc);

public record DashboardSummaryEvent(
    decimal TotalPnl, int TotalPositions, int ActiveStrategies,
    decimal DailyPnl, decimal WinRate, DateTime LastUpdateAtUtc);

public record ExchangeConnectionChangedEvent(
    Guid ExchangeId, Guid TraderId, string OldStatus,
    string NewStatus, string? ErrorMessage, DateTime ChangedAtUtc);
