using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.DomainEventHandlers;

/// <summary>
/// 交易所状态变更 → 桥接到交易事件管道，推送 UI 通知。
/// Exchange 是全局资源，不归属某个 trader，故 traderId 传 Guid.Empty。
/// </summary>
internal sealed class ExchangeConnectionChangedHandler(
    IDomainEventBus eventBus,
    ILogger<ExchangeConnectionChangedHandler> logger)
    : IDomainEventHandler<ExchangeConnectionChangedDomainEvent>
{
    public async Task HandleAsync(ExchangeConnectionChangedDomainEvent evt, CancellationToken ct)
    {
        var traderId = Guid.Empty;
        await eventBus.PublishAsync(new ExchangeConnectionChangedPayload(
            evt.ExchangeId, traderId, evt.OldStatus, evt.NewStatus,
            null, DateTime.UtcNow), ct);

        logger.LogInformation("交易所状态变更已推送: {Id} {Old} → {New}",
            evt.ExchangeId, evt.OldStatus, evt.NewStatus);
    }
}
