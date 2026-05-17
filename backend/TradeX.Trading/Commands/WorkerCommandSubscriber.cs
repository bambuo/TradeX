using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Trading.Streams;

namespace TradeX.Trading.Commands;

/// <summary>
/// Worker 进程消费 Redis Stream <see cref="WorkerCommandChannels.Commands"/>，按 <c>Type</c> 派发到对应 handler。
/// Streams + Consumer Group 设计保证 at-least-once 投递：
/// - 处理成功才 XACK；失败不 ACK，PEL 中保留供下次重试
/// - 启动时先消费 PEL 残留再切到新消息
/// - 多 Worker 实例时，消息只被一个 consumer 处理（同 group 内 round-robin）
/// </summary>
public sealed class WorkerCommandSubscriber(
    IConnectionMultiplexer redis,
    IEnumerable<IWorkerCommandHandler> handlers,
    ILogger<WorkerCommandSubscriber> logger) : BackgroundService
{
    private const string ConsumerGroup = "worker-cmd";
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var handlerMap = handlers.ToDictionary(h => h.CommandType, h => h, StringComparer.OrdinalIgnoreCase);
        if (handlerMap.Count == 0)
        {
            logger.LogWarning("未发现命令 handler，WorkerCommandSubscriber 不会启动");
            return;
        }

        var db = redis.GetDatabase();
        var streamKey = WorkerCommandChannels.Commands;
        var consumer = RedisStreamHelpers.DefaultConsumerName();

        await RedisStreamHelpers.EnsureConsumerGroupAsync(db, streamKey, ConsumerGroup, logger);
        logger.LogInformation("WorkerCommandSubscriber 启动: stream={Stream} group={Group} consumer={Consumer}, handlers=[{H}]",
            streamKey, ConsumerGroup, consumer, string.Join(",", handlerMap.Keys));

        await DrainPendingAsync(db, streamKey, consumer, handlerMap, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(streamKey, ConsumerGroup, consumer, ">", count: 20);
                if (entries.Length == 0)
                {
                    try { await Task.Delay(PollDelay, stoppingToken); }
                    catch (TaskCanceledException) { break; }
                    continue;
                }
                foreach (var entry in entries)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessAsync(db, streamKey, entry, handlerMap, stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "WorkerCommandSubscriber 读取异常，1s 后重试");
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
        logger.LogInformation("WorkerCommandSubscriber 已停止");
    }

    private async Task DrainPendingAsync(IDatabase db, string streamKey, string consumer,
        IDictionary<string, IWorkerCommandHandler> handlerMap, CancellationToken ct)
    {
        try
        {
            var pending = await db.StreamReadGroupAsync(streamKey, ConsumerGroup, consumer, "0", count: 100);
            if (pending.Length == 0) return;
            logger.LogInformation("WorkerCommandSubscriber 启动 PEL 清理: {Count} 条", pending.Length);
            foreach (var entry in pending)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessAsync(db, streamKey, entry, handlerMap, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WorkerCommandSubscriber PEL 清理失败");
        }
    }

    private async Task ProcessAsync(IDatabase db, string streamKey, StreamEntry entry,
        IDictionary<string, IWorkerCommandHandler> handlerMap, CancellationToken ct)
    {
        try
        {
            var raw = entry[RedisStreamHelpers.PayloadField];
            if (!raw.HasValue)
            {
                logger.LogWarning("命令无 payload, id={Id}, ACK 跳过", entry.Id);
                await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
                return;
            }
            var cmd = JsonSerializer.Deserialize<WorkerCommand>((string)raw!, Json);
            if (cmd is null)
            {
                logger.LogWarning("无法解析命令包络, id={Id}", entry.Id);
                await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
                return;
            }
            if (!handlerMap.TryGetValue(cmd.Type, out var handler))
            {
                logger.LogWarning("未知命令类型 Type={Type}, ACK 跳过", cmd.Type);
                await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
                return;
            }
            await handler.HandleAsync(cmd.ArgsJson, ct);
            await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
        }
        catch (Exception ex)
        {
            // 不 ACK → 留 PEL 重试
            logger.LogError(ex, "命令处理失败 id={Id}, 留待重试", entry.Id);
        }
    }
}
