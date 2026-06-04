using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Trading.Streams;

namespace TradeX.Trading.Backtest;

/// <summary>
/// Worker 端消费 Redis Stream <see cref="BacktestChannels.Cancellations"/>，
/// 收到取消事件后查找 <see cref="RunningBacktestTracker"/> 中对应的任务并触发取消。
///
/// 替代 <c>BacktestScheduler.PollForCancellationAsync</c> 的 DB 轮询（每 1 秒查 DB），
/// 改为事件驱动：取消事件到达后立即响应，延迟从 ~1s 降至 ~50ms。
/// </summary>
public sealed class BacktestCancellationConsumer(
    IConnectionMultiplexer redis,
    RunningBacktestTracker tracker,
    ILogger<BacktestCancellationConsumer> logger) : BackgroundService
{
    private const string ConsumerGroup = "worker-backtest-cancel";
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(50);
    private const long StaleIdleMs = 60_000;
    private static readonly TimeSpan ReclaimInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();
        var streamKey = BacktestChannels.Cancellations;
        var consumer = RedisStreamHelpers.DefaultConsumerName();

        await RedisStreamHelpers.EnsureConsumerGroupAsync(db, streamKey, ConsumerGroup, logger);
        logger.LogInformation("BacktestCancellationConsumer 启动: stream={Stream} group={Group} consumer={Consumer}",
            streamKey, ConsumerGroup, consumer);

        await DrainPendingAsync(db, streamKey, consumer, stoppingToken);

        var lastClaim = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastClaim > ReclaimInterval)
                {
                    lastClaim = DateTime.UtcNow;
                    var reclaimed = await RedisStreamHelpers.ClaimStaleAsync(db, streamKey, ConsumerGroup, consumer, StaleIdleMs);
                    foreach (var entry in reclaimed)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        await ProcessEntryAsync(db, streamKey, entry, stoppingToken);
                    }
                }

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
                    await ProcessEntryAsync(db, streamKey, entry, stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "BacktestCancellationConsumer 读取异常，1s 后重试");
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        logger.LogInformation("BacktestCancellationConsumer 已停止");
    }

    private async Task DrainPendingAsync(IDatabase db, string streamKey, string consumer, CancellationToken ct)
    {
        try
        {
            var pending = await db.StreamReadGroupAsync(streamKey, ConsumerGroup, consumer, "0", count: 100);
            if (pending.Length == 0) return;
            logger.LogInformation("BacktestCancellationConsumer PEL 清理 {Count} 条", pending.Length);
            foreach (var entry in pending)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessEntryAsync(db, streamKey, entry, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BacktestCancellationConsumer PEL 清理失败");
        }
    }

    private async Task ProcessEntryAsync(IDatabase db, string streamKey, StreamEntry entry, CancellationToken ct)
    {
        try
        {
            if (await RedisStreamHelpers.IsAlreadyProcessedAsync(db, ConsumerGroup, entry.Id))
            {
                await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
                return;
            }

            var raw = entry[RedisStreamHelpers.PayloadField];
            if (raw.HasValue && Guid.TryParseExact((string)raw!, "N", out var taskId))
            {
                if (tracker.RunningTasks.TryGetValue(taskId, out var cts))
                {
                    logger.LogInformation("取消事件驱动: 正在取消回测任务 TaskId={TaskId}", taskId);
                    cts.Cancel();
                }
                else
                {
                    logger.LogDebug("取消事件到达但任务不在运行中（可能已完成）: TaskId={TaskId}", taskId);
                }
            }
            else
            {
                logger.LogWarning("BacktestCancellationConsumer 收到无效 payload id={Id}, ACK 跳过", entry.Id);
            }

            await RedisStreamHelpers.MarkProcessedAsync(db, ConsumerGroup, entry.Id);
            await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BacktestCancellationConsumer 处理失败 id={Id}, 留待重试", entry.Id);
        }
    }
}
