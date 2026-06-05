using Microsoft.AspNetCore.SignalR;
using TradeX.Api.Hubs;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Api.DomainEventHandlers;

/// <summary>
/// 接收领域事件总线的事件并通过 SignalR 推送到前端。
/// 7 个 <c>[DomainEventHandler]</c> 方法分别处理 7 种交易事件类型。
/// </summary>
internal sealed class TradingSignalRHandler(
    IHubContext<TradingHub> hubContext,
    ILogger<TradingSignalRHandler> logger)
{
    private const string SystemAlertsGroup = "system_alerts";

    [DomainEventHandler(typeof(PositionUpdatedPayload))]
    public async Task HandlePositionUpdated(PositionUpdatedPayload payload, Guid traceId, CancellationToken ct)
    {
        await hubContext.Clients.Group(payload.TraderId.ToString("N"))
            .SendAsync(TradingHub.PositionUpdated, payload, ct);
        logger.LogDebug("SignalR 推送: PositionUpdated TraderId={Trader} PositionId={Id}",
            payload.TraderId, payload.PositionId);
    }

    [DomainEventHandler(typeof(OrderPlacedPayload))]
    public async Task HandleOrderPlaced(OrderPlacedPayload payload, Guid traceId, CancellationToken ct)
    {
        await hubContext.Clients.Group(payload.TraderId.ToString("N"))
            .SendAsync(TradingHub.OrderPlaced, payload, ct);
        logger.LogDebug("SignalR 推送: OrderPlaced TraderId={Trader} OrderId={Id}",
            payload.TraderId, payload.OrderId);
    }

    [DomainEventHandler(typeof(BindingStatusChangedPayload))]
    public async Task HandleBindingStatusChanged(BindingStatusChangedPayload payload, Guid traceId, CancellationToken ct)
    {
        await hubContext.Clients.Group(payload.TraderId.ToString("N"))
            .SendAsync(TradingHub.BindingStatusChanged, payload, ct);
        logger.LogInformation("SignalR 推送: BindingStatusChanged TraderId={Trader} {Old}→{New}",
            payload.TraderId, payload.OldStatus, payload.NewStatus);
    }

    [DomainEventHandler(typeof(RiskAlertPayload))]
    public async Task HandleRiskAlert(RiskAlertPayload payload, Guid traceId, CancellationToken ct)
    {
        // 风控告警也推送到 trader 私人群
        await hubContext.Clients.Group(payload.TraderId.ToString("N"))
            .SendAsync(TradingHub.RiskAlert, payload, ct);
        logger.LogWarning("SignalR 推送: RiskAlert TraderId={Trader} Level={Level} Category={Category}",
            payload.TraderId, payload.Level, payload.Category);
    }

    [DomainEventHandler(typeof(DashboardSummaryPayload))]
    public async Task HandleDashboardSummary(DashboardSummaryPayload payload, Guid traceId, CancellationToken ct)
    {
        // 仪表盘摘要推送到订阅了系统组的管理员
        await hubContext.Clients.Group(SystemAlertsGroup)
            .SendAsync(TradingHub.DashboardSummary, payload, ct);
    }

    [DomainEventHandler(typeof(ExchangeConnectionChangedPayload))]
    public async Task HandleExchangeConnectionChanged(ExchangeConnectionChangedPayload payload, Guid traceId, CancellationToken ct)
    {
        await hubContext.Clients.Group(payload.TraderId.ToString("N"))
            .SendAsync(TradingHub.ExchangeConnectionChanged, payload, ct);
        logger.LogInformation("SignalR 推送: ExchangeConnectionChanged TraderId={Trader} {Old}→{New}",
            payload.TraderId, payload.OldStatus, payload.NewStatus);
    }

    [DomainEventHandler(typeof(OrphanOrderDetectedPayload))]
    public async Task HandleOrphanOrderDetected(OrphanOrderDetectedPayload payload, Guid traceId, CancellationToken ct)
    {
        // 孤儿订单无 trader 归属，推送到系统告警组
        await hubContext.Clients.Group(SystemAlertsGroup)
            .SendAsync(TradingHub.OrphanOrderDetected, payload, ct);
        logger.LogWarning("SignalR 推送: OrphanOrderDetected ExchangeId={Ex} OrderId={Ord}",
            payload.ExchangeId, payload.ExchangeOrderId);
    }
}
