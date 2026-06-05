using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

internal sealed class OrderFilledHandler(
    IDomainEventBus eventBus,
    ILogger<OrderFilledHandler> logger)
    : IDomainEventHandler<OrderFilledDomainEvent>
{
    public async Task HandleAsync(OrderFilledDomainEvent evt, CancellationToken ct)
    {
        // 成交事件同时推 OrderPlaced（带 Filled 状态）让前端更新订单列表
        await eventBus.PublishAsync(new OrderPlacedPayload(
            evt.OrderId, evt.TraderId, Guid.Empty, null,
            evt.Pair, evt.Side, "", "Filled",
            0, evt.OccurredAt), ct);

        logger.LogDebug("订单成交事件已推送: OrderId={Id} Trader={Trader} FillQty={Qty}",
            evt.OrderId, evt.TraderId, evt.FilledQuantity);
    }
}
