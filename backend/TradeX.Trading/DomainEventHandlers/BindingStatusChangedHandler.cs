using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

internal sealed class BindingStatusChangedHandler(
    IDomainEventBus eventBus,
    ILogger<BindingStatusChangedHandler> logger)
    : IDomainEventHandler<BindingStatusChangedDomainEvent>
{
    public async Task HandleAsync(BindingStatusChangedDomainEvent evt, CancellationToken ct)
    {
        await eventBus.PublishAsync(new BindingStatusChangedPayload(
            evt.BindingId, evt.TraderId, evt.OldStatus, evt.NewStatus,
            null, DateTime.UtcNow), ct);

        logger.LogInformation("策略绑定状态变更已推送: BindingId={Id} {Old} → {New}",
            evt.BindingId, evt.OldStatus, evt.NewStatus);
    }
}
