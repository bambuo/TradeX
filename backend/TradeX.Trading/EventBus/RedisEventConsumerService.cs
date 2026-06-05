using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Trading.Streams;

namespace TradeX.Trading.EventBus;

/// <summary>
/// Redis Stream 消费者后台服务。使用 XREADGROUP 从 <c>tradex:events</c> 消费事件，
/// 反序列化后交给 <see cref="EventConsumerService"/> 分发。
/// </summary>
internal sealed class RedisEventConsumerService(
    IConnectionMultiplexer redis,
    EventConsumerService dispatcher,
    ILogger<RedisEventConsumerService> logger)
    : BackgroundService
{
    private const string StreamKey = "tradex:events";
    private const string ConsumerGroup = "tradex:event-consumers";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private const int BatchSize = 10;
    private const long StaleIdleMs = 60_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        dispatcher.EnsureInitialized();
        var db = redis.GetDatabase();
        var consumer = RedisStreamHelpers.DefaultConsumerName();

        // 确保 consumer group 存在（幂等）
        await RedisStreamHelpers.EnsureConsumerGroupAsync(db, StreamKey, ConsumerGroup, logger);

        // 启动时回收本组内其他僵死 consumer 的 PEL 消息
        var reclaimed = await RedisStreamHelpers.ClaimStaleAsync(
            db, StreamKey, ConsumerGroup, consumer, StaleIdleMs);
        if (reclaimed.Length > 0)
            logger.LogInformation("回收了 {Count} 条 PEL 遗留消息", reclaimed.Length);

        logger.LogInformation("Redis 事件消费者已启动: consumer={Consumer}", consumer);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(
                    StreamKey, ConsumerGroup, consumer,
                    default, BatchSize);

                foreach (var entry in entries)
                {
                    // 去重：跳过已被本组其他 consumer 处理的条目
                    if (await RedisStreamHelpers.IsAlreadyProcessedAsync(db, ConsumerGroup, entry.Id))
                    {
                        await db.StreamAcknowledgeAsync(StreamKey, ConsumerGroup, entry.Id);
                        continue;
                    }

                    var raw = entry[RedisStreamHelpers.PayloadField];
                    if (raw.IsNullOrEmpty) continue;

                    var envelope = System.Text.Json.JsonSerializer.Deserialize<EventEnvelope>((string)raw!, DomainEventBusBase.JsonOptions);
                    if (envelope is null) continue;

                    if (string.IsNullOrEmpty(envelope.EventType))
                    {
                        logger.LogWarning("事件载荷缺少 EventType，已跳过");
                        await db.StreamAcknowledgeAsync(StreamKey, ConsumerGroup, entry.Id);
                        await RedisStreamHelpers.MarkProcessedAsync(db, ConsumerGroup, entry.Id);
                        continue;
                    }

                    var eventType = Type.GetType(envelope.EventType);
                    if (eventType is null)
                    {
                        logger.LogWarning("未知事件类型: {Type}", envelope.EventType);
                        await db.StreamAcknowledgeAsync(StreamKey, ConsumerGroup, entry.Id);
                        await RedisStreamHelpers.MarkProcessedAsync(db, ConsumerGroup, entry.Id);
                        continue;
                    }

                    await dispatcher.DispatchAsync(eventType, envelope.DataJson, envelope.TraceId, stoppingToken);

                    // 分发成功后 ACK + 标记去重
                    await db.StreamAcknowledgeAsync(StreamKey, ConsumerGroup, entry.Id);
                    await RedisStreamHelpers.MarkProcessedAsync(db, ConsumerGroup, entry.Id);
                }

                if (entries.Length == 0)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Redis 事件消费循环异常");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
