using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

internal sealed class OrderPlacedHandler(
    IDomainEventBus eventBus,
    ILogger<OrderPlacedHandler> logger)
    : IDomainEventHandler<OrderPlacedDomainEvent>
{
    public async Task HandleAsync(OrderPlacedDomainEvent evt, CancellationToken ct)
    {
        await eventBus.PublishAsync(new OrderPlacedPayload(
            evt.OrderId, evt.TraderId, evt.ExchangeId, evt.StrategyId,
            evt.Pair, evt.Side, evt.Type, "Pending",
            evt.Quantity, evt.OccurredAt), ct);

        logger.LogDebug("订单已创建事件已推送: OrderId={Id} Trader={Trader}",
            evt.OrderId, evt.TraderId);
    }
}
