using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using TradeX.Api.Hubs;
using TradeX.Trading.Events;
using TradeX.Trading.Streams;

namespace TradeX.Api.Services;

/// <summary>
/// 订阅 Redis Stream <see cref="TradingEventChannels.Events"/>（Worker 发布的业务事件），
/// 按 <c>Type</c> 解码 envelope 后转发到对应 SignalR group <c>trader_{traderId}</c>。
///
/// SignalR 自身用 Redis backplane（独立频道，由 Microsoft.AspNetCore.SignalR.StackExchangeRedis 托管），
/// 与本类订阅的 stream 不冲突。
///
/// <b>Streams + Consumer Group 设计</b>：
/// - Consumer group = <c>api-signalr-bridge</c>
/// - Consumer name = 当前主机名（重启后 PEL 仍归本 consumer）
/// - 处理成功才 XACK；失败保留在 PEL，下次重新投递（自带 at-least-once）
/// - 启动时先 XREADGROUP "0" 清理上次未确认的 PEL，再切到 ">" 长轮询
/// </summary>
public sealed class RedisToSignalRBridge(
    IConnectionMultiplexer redis,
    IHubContext<TradingHub> hub,
    ILogger<RedisToSignalRBridge> logger) : BackgroundService
{
    private const string ConsumerGroup = "api-signalr-bridge";
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();
        var streamKey = TradingEventChannels.Events;
        var consumer = RedisStreamHelpers.DefaultConsumerName();

        await RedisStreamHelpers.EnsureConsumerGroupAsync(db, streamKey, ConsumerGroup, logger);
        logger.LogInformation("RedisToSignalRBridge 订阅: stream={Stream} group={Group} consumer={Consumer}",
            streamKey, ConsumerGroup, consumer);

        // 阶段 1: 清理上次未 ACK 的 PEL（重启恢复）
        await DrainPendingAsync(db, streamKey, consumer, stoppingToken);

        // 阶段 2: 长轮询新消息
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(streamKey, ConsumerGroup, consumer, ">", count: 50);
                if (entries.Length == 0)
                {
                    try { await Task.Delay(PollDelay, stoppingToken); }
                    catch (TaskCanceledException) { break; }
                    continue;
                }
                foreach (var entry in entries)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessAsync(db, streamKey, entry);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "RedisToSignalRBridge 读取异常，1s 后重试");
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
        logger.LogInformation("RedisToSignalRBridge 已停止");
    }

    private async Task DrainPendingAsync(IDatabase db, string streamKey, string consumer, CancellationToken ct)
    {
        try
        {
            var pending = await db.StreamReadGroupAsync(streamKey, ConsumerGroup, consumer, "0", count: 100);
            if (pending.Length == 0) return;
            logger.LogInformation("启动 PEL 清理: {Count} 条未 ACK 消息", pending.Length);
            foreach (var entry in pending)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessAsync(db, streamKey, entry);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PEL 清理失败，跳过");
        }
    }

    private async Task ProcessAsync(IDatabase db, string streamKey, StreamEntry entry)
    {
        try
        {
            var payload = entry[RedisStreamHelpers.PayloadField];
            if (!payload.HasValue)
            {
                logger.LogWarning("消息无 {Field} 字段, id={Id}, 跳过并 ACK", RedisStreamHelpers.PayloadField, entry.Id);
                await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
                return;
            }
            await HandleAsync((string)payload!);
            await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
        }
        catch (Exception ex)
        {
            // 处理异常 → 不 ACK → 留在 PEL → 下次读取重投
            logger.LogError(ex, "Bridge 处理消息失败 id={Id}, 留待重试", entry.Id);
        }
    }

    private async Task HandleAsync(string raw)
    {
        var envelope = JsonSerializer.Deserialize<TradingEventEnvelope>(raw, Json);
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
}
