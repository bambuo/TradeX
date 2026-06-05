using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

internal sealed class OrderCancelledHandler(
    IDomainEventBus eventBus,
    ILogger<OrderCancelledHandler> logger)
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public async Task HandleAsync(OrderCancelledDomainEvent evt, CancellationToken ct)
    {
        await eventBus.PublishAsync(new OrderPlacedPayload(
            evt.OrderId, evt.TraderId, Guid.Empty, null,
            "", "", "", "Cancelled",
            0, evt.OccurredAt), ct);

        logger.LogDebug("订单取消事件已推送: OrderId={Id} Trader={Trader}",
            evt.OrderId, evt.TraderId);
    }
}
