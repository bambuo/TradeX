using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

internal sealed class PositionOpenedHandler(
    IDomainEventBus eventBus,
    ILogger<PositionOpenedHandler> logger)
    : IDomainEventHandler<PositionOpenedDomainEvent>
{
    public async Task HandleAsync(PositionOpenedDomainEvent evt, CancellationToken ct)
    {
        await eventBus.PublishAsync(new PositionUpdatedPayload(
            evt.PositionId, evt.TraderId, Guid.Empty, evt.StrategyId,
            evt.Pair, evt.Quantity, evt.EntryPrice, 0, 0,
            "Open", evt.OccurredAt), ct);

        logger.LogInformation("持仓开仓事件已推送: PositionId={Id} Trader={Trader} Pair={Pair}",
            evt.PositionId, evt.TraderId, evt.Pair);
    }
}
