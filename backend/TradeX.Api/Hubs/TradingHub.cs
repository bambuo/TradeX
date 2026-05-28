using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Hubs;

[Microsoft.AspNetCore.Authorization.Authorize]
public class TradingHub(
    ITraderRepository traderRepo,
    ILogger<TradingHub> logger) : Hub
{
    public const string PositionUpdated = "PositionUpdated";
    public const string OrderPlaced = "OrderPlaced";
    public const string BindingStatusChanged = "BindingStatusChanged";
    public const string RiskAlert = "RiskAlert";
    public const string DashboardSummary = "DashboardSummary";
    public const string ExchangeConnectionChanged = "ExchangeConnectionChanged";
    public const string OrphanOrderDetected = "OrphanOrderDetected";

    /// <summary>系统级告警组（孤儿订单等无 trader 归属的事件）。管理员连接时自动加入。</summary>
    public const string SystemAlertsGroup = "system_alerts";

    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (role is "SuperAdmin" or "Admin")
            await Groups.AddToGroupAsync(Context.ConnectionId, SystemAlertsGroup);
        await base.OnConnectedAsync();
    }

    public async Task JoinTraderGroup(string traderId)
    {
        if (!Guid.TryParse(traderId, out var parsedTraderId))
        {
            logger.LogWarning("SignalR trader group join rejected: invalid TraderId={TraderId}", traderId);
            throw new HubException("无效的交易员标识");
        }

        if (!await CanAccessTraderAsync(parsedTraderId, Context.ConnectionAborted))
        {
            logger.LogWarning("SignalR trader group join rejected: ConnectionId={ConnectionId}, TraderId={TraderId}",
                Context.ConnectionId, parsedTraderId);
            throw new HubException("无权订阅该交易员事件");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"trader_{parsedTraderId}");
    }

    public async Task LeaveTraderGroup(string traderId)
    {
        if (!Guid.TryParse(traderId, out var parsedTraderId))
            return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trader_{parsedTraderId}");
    }

    private async Task<bool> CanAccessTraderAsync(Guid traderId, CancellationToken ct)
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return false;

        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (role is "SuperAdmin" or "Admin")
            return true;

        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        return trader?.UserId == userId;
    }
}

public record PositionUpdatedEvent(
    Guid PositionId, Guid TraderId, Guid ExchangeId, Guid StrategyId,
    string Pair, decimal Quantity, decimal EntryPrice, decimal UnrealizedPnl,
    decimal RealizedPnl, string Status, DateTime UpdatedAt,
    Guid TraceId);

public record OrderPlacedEvent(
    Guid OrderId, Guid TraderId, Guid ExchangeId, Guid? StrategyId,
    string Pair, string Side, string Type, string Status,
    decimal Quantity, DateTime PlacedAtUtc,
    Guid TraceId);

public record BindingStatusChangedEvent(
    Guid StrategyId, Guid TraderId, string OldStatus, string NewStatus,
    string? Reason, DateTime ChangedAtUtc,
    Guid TraceId);

public record RiskAlertEvent(
    Guid AlertId, string Level, string Category, Guid TraderId,
    Guid? StrategyId, string Message, DateTime TriggeredAtUtc,
    Guid TraceId);

public record DashboardSummaryEvent(
    decimal TotalPnl, int TotalPositions, int ActiveStrategies,
    decimal DailyPnl, decimal WinRate, DateTime LastUpdateAtUtc,
    Guid TraceId);

public record ExchangeConnectionChangedEvent(
    Guid ExchangeId, Guid TraderId, string OldStatus,
    string NewStatus, string? ErrorMessage, DateTime ChangedAtUtc,
    Guid TraceId);

public record OrphanOrderDetectedEvent(
    Guid ExchangeId, string ExchangeType, string Pair, string ExchangeOrderId,
    string Side, string Type, decimal Price, decimal Quantity, DateTime DetectedAt,
    Guid TraceId);
