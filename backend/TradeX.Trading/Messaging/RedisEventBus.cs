using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Trading.Events;

namespace TradeX.Trading.Messaging;

/// <summary>
/// 通过 Redis Pub/Sub 跨进程发布交易事件。Worker 端使用；API 端的
/// <c>RedisToSignalRBridge</c> 订阅同一频道并转发到 SignalR Hub。
///
/// 频道：<see cref="TradingEventChannels.Events"/> ("tradex:events")。
/// 单频道 + 类型标签 envelope，方便后续扩展。
/// </summary>
public sealed class RedisEventBus(
    IConnectionMultiplexer redis,
    ILogger<RedisEventBus> logger) : ITradingEventBus
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task PublishAsync<T>(string type, Guid traderId, T payload, Guid traceId)
    {
        try
        {
            var envelope = new TradingEventEnvelope(
                Type: type,
                TraceId: traceId,
                TraderId: traderId,
                DataJson: JsonSerializer.Serialize(payload, Json));
            var json = JsonSerializer.Serialize(envelope, Json);
            await redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(TradingEventChannels.Events),
                json);
        }
        catch (Exception ex)
        {
            // 事件丢失是可容忍降级（DB 仍是真相）；记录后吞掉，避免业务流程因事件总线失败而失败
            logger.LogWarning(ex, "Redis 事件发布失败, Type={Type}, TraderId={Trader}", type, traderId);
        }
    }

    public Task PositionUpdatedAsync(Guid traderId, Guid positionId, Guid exchangeId, Guid strategyId,
        string Pair, decimal quantity, decimal entryPrice, decimal unrealizedPnl,
        decimal realizedPnl, string status, DateTime updatedAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
        => PublishAsync(TradingEventTypes.PositionUpdated, traderId, new PositionUpdatedPayload(
            positionId, traderId, exchangeId, strategyId, Pair, quantity,
            entryPrice, unrealizedPnl, realizedPnl, status, updatedAtUtc),
            traceId ?? Guid.NewGuid());

    public Task OrderPlacedAsync(Guid traderId, Guid orderId, Guid exchangeId, Guid? strategyId,
        string Pair, string side, string type, string status,
        decimal quantity, DateTime placedAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
        => PublishAsync(TradingEventTypes.OrderPlaced, traderId, new OrderPlacedPayload(
            orderId, traderId, exchangeId, strategyId, Pair, side, type, status, quantity, placedAtUtc),
            traceId ?? Guid.NewGuid());

    public Task BindingStatusChangedAsync(Guid traderId, Guid strategyId, string oldStatus,
        string newStatus, string? reason,
        CancellationToken ct = default, Guid? traceId = null)
        => PublishAsync(TradingEventTypes.BindingStatusChanged, traderId, new BindingStatusChangedPayload(
            strategyId, traderId, oldStatus, newStatus, reason, DateTime.UtcNow),
            traceId ?? Guid.NewGuid());

    public Task RiskAlertAsync(Guid traderId, string level, string category, Guid? strategyId,
        string message,
        CancellationToken ct = default, Guid? traceId = null)
        => PublishAsync(TradingEventTypes.RiskAlert, traderId, new RiskAlertPayload(
            Guid.NewGuid(), level, category, traderId, strategyId, message, DateTime.UtcNow),
            traceId ?? Guid.NewGuid());

    public Task DashboardSummaryAsync(Guid traderId, decimal totalPnl, int totalPositions,
        int activeStrategies, decimal dailyPnl, decimal winRate,
        DateTime lastUpdateAtUtc,
        CancellationToken ct = default, Guid? traceId = null)
        => PublishAsync(TradingEventTypes.DashboardSummary, traderId, new DashboardSummaryPayload(
            totalPnl, totalPositions, activeStrategies, dailyPnl, winRate, lastUpdateAtUtc),
            traceId ?? Guid.NewGuid());

    public Task ExchangeConnectionChangedAsync(Guid traderId, Guid exchangeId, string oldStatus,
        string newStatus, string? errorMessage,
        CancellationToken ct = default, Guid? traceId = null)
        => PublishAsync(TradingEventTypes.ExchangeConnectionChanged, traderId, new ExchangeConnectionChangedPayload(
            exchangeId, traderId, oldStatus, newStatus, errorMessage, DateTime.UtcNow),
            traceId ?? Guid.NewGuid());
}
