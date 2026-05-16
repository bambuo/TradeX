using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using TradeX.Api.Hubs;
using TradeX.Trading.Events;

namespace TradeX.Api.Services;

/// <summary>
/// 桥接：订阅 Redis 频道 <see cref="TradingEventChannels.Events"/>（Worker 发布的业务事件），
/// 按 <c>Type</c> 解码 envelope 后转发到对应 SignalR group <c>trader_{traderId}</c>。
///
/// SignalR 本身用 Redis backplane（独立频道，由 Microsoft.AspNetCore.SignalR.StackExchangeRedis 托管），
/// 与本类订阅的 <c>tradex:events</c> 不冲突。
///
/// ⚠️ 多 API 实例下需改用 Redis Streams + Consumer Group 防止事件重复转发；当前 Pub/Sub 实现假定 API 单实例。
/// </summary>
public sealed class RedisToSignalRBridge(
    IConnectionMultiplexer redis,
    IHubContext<TradingHub> hub,
    ILogger<RedisToSignalRBridge> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Literal(TradingEventChannels.Events);

        var queue = await subscriber.SubscribeAsync(channel);
        queue.OnMessage(msg =>
        {
            // 用 fire-and-forget 处理，避免阻塞 Redis IO 线程；异常在内部捕获
            _ = HandleAsync(msg.Message);
        });

        logger.LogInformation("RedisToSignalRBridge 订阅成功: Channel={Channel}", TradingEventChannels.Events);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException) { /* normal shutdown */ }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
            logger.LogInformation("RedisToSignalRBridge 已停止");
        }
    }

    private async Task HandleAsync(RedisValue raw)
    {
        if (!raw.HasValue) return;
        try
        {
            var envelope = JsonSerializer.Deserialize<TradingEventEnvelope>((string)raw!, Json);
            if (envelope is null) return;
            var group = hub.Clients.Group($"trader_{envelope.TraderId}");

            switch (envelope.Type)
            {
                case TradingEventTypes.PositionUpdated:
                {
                    var p = JsonSerializer.Deserialize<PositionUpdatedPayload>(envelope.DataJson, Json);
                    if (p is null) return;
                    await group.SendAsync(TradingHub.PositionUpdated, new PositionUpdatedEvent(
                        p.PositionId, p.TraderId, p.ExchangeId, p.StrategyId, p.Pair, p.Quantity,
                        p.EntryPrice, p.UnrealizedPnl, p.RealizedPnl, p.Status, p.UpdatedAt));
                    break;
                }
                case TradingEventTypes.OrderPlaced:
                {
                    var p = JsonSerializer.Deserialize<OrderPlacedPayload>(envelope.DataJson, Json);
                    if (p is null) return;
                    await group.SendAsync(TradingHub.OrderPlaced, new OrderPlacedEvent(
                        p.OrderId, p.TraderId, p.ExchangeId, p.StrategyId, p.Pair,
                        p.Side, p.Type, p.Status, p.Quantity, p.PlacedAtUtc));
                    break;
                }
                case TradingEventTypes.BindingStatusChanged:
                {
                    var p = JsonSerializer.Deserialize<BindingStatusChangedPayload>(envelope.DataJson, Json);
                    if (p is null) return;
                    await group.SendAsync(TradingHub.BindingStatusChanged, new BindingStatusChangedEvent(
                        p.StrategyId, p.TraderId, p.OldStatus, p.NewStatus, p.Reason, p.ChangedAtUtc));
                    break;
                }
                case TradingEventTypes.RiskAlert:
                {
                    var p = JsonSerializer.Deserialize<RiskAlertPayload>(envelope.DataJson, Json);
                    if (p is null) return;
                    await group.SendAsync(TradingHub.RiskAlert, new RiskAlertEvent(
                        p.AlertId, p.Level, p.Category, p.TraderId, p.StrategyId, p.Message, p.TriggeredAtUtc));
                    break;
                }
                case TradingEventTypes.DashboardSummary:
                {
                    var p = JsonSerializer.Deserialize<DashboardSummaryPayload>(envelope.DataJson, Json);
                    if (p is null) return;
                    await group.SendAsync(TradingHub.DashboardSummary, new DashboardSummaryEvent(
                        p.TotalPnl, p.TotalPositions, p.ActiveStrategies, p.DailyPnl, p.WinRate, p.LastUpdateAtUtc));
                    break;
                }
                case TradingEventTypes.ExchangeConnectionChanged:
                {
                    var p = JsonSerializer.Deserialize<ExchangeConnectionChangedPayload>(envelope.DataJson, Json);
                    if (p is null) return;
                    await group.SendAsync(TradingHub.ExchangeConnectionChanged, new ExchangeConnectionChangedEvent(
                        p.ExchangeId, p.TraderId, p.OldStatus, p.NewStatus, p.ErrorMessage, p.ChangedAtUtc));
                    break;
                }
                default:
                    logger.LogWarning("收到未知事件类型 Type={Type}, 忽略", envelope.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RedisToSignalRBridge 处理消息异常");
        }
    }
}
