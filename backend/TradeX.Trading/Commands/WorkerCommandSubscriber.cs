using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TradeX.Trading.Commands;

/// <summary>
/// Worker 进程订阅 <see cref="WorkerCommandChannels.Commands"/>，按 <c>Type</c> 派发到对应 handler。
/// 每个命令在新的后台 Task 中处理，避免阻塞 Redis IO 线程；handler 内部异常不会影响订阅。
/// </summary>
public sealed class WorkerCommandSubscriber(
    IConnectionMultiplexer redis,
    IEnumerable<IWorkerCommandHandler> handlers,
    ILogger<WorkerCommandSubscriber> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var handlerMap = handlers.ToDictionary(h => h.CommandType, h => h, StringComparer.OrdinalIgnoreCase);
        if (handlerMap.Count == 0)
        {
            logger.LogWarning("未发现命令 handler，WorkerCommandSubscriber 不会订阅");
            return;
        }

        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Literal(WorkerCommandChannels.Commands);
        var queue = await subscriber.SubscribeAsync(channel);
        queue.OnMessage(msg => _ = HandleAsync(msg.Message, handlerMap, stoppingToken));

        logger.LogInformation("WorkerCommandSubscriber 订阅: Channel={Channel}, Handlers=[{Handlers}]",
            WorkerCommandChannels.Commands, string.Join(",", handlerMap.Keys));

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (TaskCanceledException) { /* shutting down */ }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
            logger.LogInformation("WorkerCommandSubscriber 已停止");
        }
    }

    private async Task HandleAsync(RedisValue raw, IDictionary<string, IWorkerCommandHandler> handlerMap, CancellationToken ct)
    {
        if (!raw.HasValue) return;
        try
        {
            var cmd = JsonSerializer.Deserialize<WorkerCommand>((string)raw!, Json);
            if (cmd is null)
            {
                logger.LogWarning("无法解析命令包络: {Raw}", (string)raw!);
                return;
            }
            if (!handlerMap.TryGetValue(cmd.Type, out var handler))
            {
                logger.LogWarning("未知命令类型，忽略: Type={Type}", cmd.Type);
                return;
            }
            await handler.HandleAsync(cmd.ArgsJson, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理命令异常");
        }
    }
}
