using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Events;
using TradeX.Trading.Messaging;

namespace TradeX.Trading.Outbox;

/// <summary>
/// ITradingEventBus 实现：写入 outbox 表（业务事务的一部分）而非直接 Publish。
/// 配合 <see cref="OutboxRelayService"/>：业务 SaveChanges 提交后，relay 才会看到行，确保
/// "事件发布"与"业务写入"的原子性。
///
/// 注意：<b>调用方必须在同一 DbContext 事务内 SaveChanges</b>，否则 outbox 行不会落库。
/// </summary>
public sealed class OutboxTradingEventBus(
    IOutboxRepository outboxRepo,
    ILogger<OutboxTradingEventBus> logger) : ITradingEventBus
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task EnqueueAsync<T>(string type, Guid traderId, T payload, Guid traceId, CancellationToken ct)
    {
        var envelope = new TradingEventEnvelope(
            Type: type,
            TraceId: traceId,
            TraderId: traderId,
            DataJson: JsonSerializer.Serialize(payload, Json));
        var evt = new OutboxEvent
        {
            Type = type,
            TraderId = traderId,
            PayloadJson = JsonSerializer.Serialize(envelope, Json),
            Status = OutboxStatus.Pending,
        };
        await outboxRepo.EnqueueAsync(evt, ct);
        // 立即提交 outbox 行。若调用方开启显式事务，此提交仍随同一事务提交/回滚。
        await outboxRepo.SaveChangesAsync(ct);
        logger.LogDebug("Outbox enqueued: Type={Type} TraderId={Trader} TraceId={Trace}", type, traderId, traceId);
    }

    public Task PositionUpdatedAsync(Guid traderId, Guid positionId, Guid exchangeId, Guid strategyId,
        string Pair, decimal quantity, decimal entryPrice, decimal unrealizedPnl,
        decimal realizedPnl, string status, DateTime updatedAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
        => EnqueueAsync(TradingEventTypes.PositionUpdated, traderId, new PositionUpdatedPayload(
            positionId, traderId, exchangeId, strategyId, Pair, quantity,
            entryPrice, unrealizedPnl, realizedPnl, status, updatedAtUtc),
            traceId ?? Guid.NewGuid(), ct);

    public Task OrderPlacedAsync(Guid traderId, Guid orderId, Guid exchangeId, Guid? strategyId,
        string Pair, string side, string type, string status,
        decimal quantity, DateTime placedAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
        => EnqueueAsync(TradingEventTypes.OrderPlaced, traderId, new OrderPlacedPayload(
            orderId, traderId, exchangeId, strategyId, Pair, side, type, status, quantity, placedAtUtc),
            traceId ?? Guid.NewGuid(), ct);

    public Task BindingStatusChangedAsync(Guid traderId, Guid strategyId, string oldStatus,
        string newStatus, string? reason,
        CancellationToken ct = default, Guid? traceId = null)
        => EnqueueAsync(TradingEventTypes.BindingStatusChanged, traderId, new BindingStatusChangedPayload(
            strategyId, traderId, oldStatus, newStatus, reason, DateTime.UtcNow),
            traceId ?? Guid.NewGuid(), ct);

    public Task RiskAlertAsync(Guid traderId, string level, string category, Guid? strategyId,
        string message,
        CancellationToken ct = default, Guid? traceId = null)
        => EnqueueAsync(TradingEventTypes.RiskAlert, traderId, new RiskAlertPayload(
            Guid.NewGuid(), level, category, traderId, strategyId, message, DateTime.UtcNow),
            traceId ?? Guid.NewGuid(), ct);

    public Task DashboardSummaryAsync(Guid traderId, decimal totalPnl, int totalPositions,
        int activeStrategies, decimal dailyPnl, decimal winRate,
        DateTime lastUpdateAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
        => EnqueueAsync(TradingEventTypes.DashboardSummary, traderId, new DashboardSummaryPayload(
            totalPnl, totalPositions, activeStrategies, dailyPnl, winRate, lastUpdateAtUtc),
            traceId ?? Guid.NewGuid(), ct);

    public Task ExchangeConnectionChangedAsync(Guid traderId, Guid exchangeId, string oldStatus,
        string newStatus, string? errorMessage,
        CancellationToken ct = default, Guid? traceId = null)
        => EnqueueAsync(TradingEventTypes.ExchangeConnectionChanged, traderId, new ExchangeConnectionChangedPayload(
            exchangeId, traderId, oldStatus, newStatus, errorMessage, DateTime.UtcNow),
            traceId ?? Guid.NewGuid(), ct);
}
