using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

internal sealed class PositionClosedHandler(
    IDomainEventBus eventBus,
    ILogger<PositionClosedHandler> logger)
    : IDomainEventHandler<PositionClosedDomainEvent>
{
    public async Task HandleAsync(PositionClosedDomainEvent evt, CancellationToken ct)
    {
        await eventBus.PublishAsync(new PositionUpdatedPayload(
            evt.PositionId, evt.TraderId, Guid.Empty, Guid.Empty,
            evt.Pair, evt.Quantity, evt.EntryPrice, 0, evt.RealizedPnl,
            "Closed", evt.OccurredAt), ct);

        logger.LogInformation("持仓平仓事件已推送: PositionId={Id} Trader={Trader} Pnl={Pnl}",
            evt.PositionId, evt.TraderId, evt.RealizedPnl);
    }
}
