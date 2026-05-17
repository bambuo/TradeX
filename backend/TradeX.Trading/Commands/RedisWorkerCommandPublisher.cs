using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Trading.Streams;

namespace TradeX.Trading.Commands;

public sealed class RedisWorkerCommandPublisher(
    IConnectionMultiplexer redis,
    ILogger<RedisWorkerCommandPublisher> logger) : IWorkerCommandPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(string type, object? args = null, CancellationToken ct = default)
    {
        var envelope = new WorkerCommand(type, args is null ? "{}" : JsonSerializer.Serialize(args, Json));
        var payload = JsonSerializer.Serialize(envelope, Json);
        var id = await RedisStreamHelpers.AddAsync(redis.GetDatabase(), WorkerCommandChannels.Commands, payload);
        logger.LogInformation("命令已发布: Type={Type}, StreamId={Id}", type, id);
    }
}

/// <summary>Redis 未配置时的降级实现，仅记日志。</summary>
public sealed class NullWorkerCommandPublisher(ILogger<NullWorkerCommandPublisher> logger) : IWorkerCommandPublisher
{
    public Task PublishAsync(string type, object? args = null, CancellationToken ct = default)
    {
        logger.LogWarning("Redis 未配置，命令未派发: Type={Type}", type);
        return Task.CompletedTask;
    }
}
