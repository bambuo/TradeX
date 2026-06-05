using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Trading.Streams;

namespace TradeX.Trading.EventBus;

/// <summary>
/// Redis Stream 实现的生产者端。使用 XADD 将事件追加到 <c>tradex:events</c> 流。
/// </summary>
public sealed class RedisDomainEventBus(
    IConnectionMultiplexer redis,
    ILogger<RedisDomainEventBus> logger)
    : DomainEventBusBase
{
    private const string StreamKey = "tradex:events";

    protected override async Task PublishCoreAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(envelope, JsonOptions);
        var db = redis.GetDatabase();
        await RedisStreamHelpers.AddAsync(db, StreamKey, payload);

        logger.LogDebug("领域事件已发布: Type={Type} TraceId={TraceId}",
            envelope.EventType, envelope.TraceId);
    }
}
