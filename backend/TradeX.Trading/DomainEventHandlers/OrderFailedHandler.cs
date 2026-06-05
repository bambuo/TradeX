using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

internal sealed class OrderFailedHandler(
    IDomainEventBus eventBus,
    ILogger<OrderFailedHandler> logger)
    : IDomainEventHandler<OrderFailedDomainEvent>
{
    public async Task HandleAsync(OrderFailedDomainEvent evt, CancellationToken ct)
    {
        await eventBus.PublishAsync(new OrderPlacedPayload(
            evt.OrderId, evt.TraderId, Guid.Empty, null,
            "", "", "", "Failed",
            0, evt.OccurredAt), ct);

        logger.LogWarning("订单失败事件已推送: OrderId={Id} Trader={Trader} Reason={Reason}",
            evt.OrderId, evt.TraderId, evt.Reason);
    }
}
