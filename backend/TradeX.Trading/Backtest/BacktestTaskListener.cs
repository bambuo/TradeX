using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Streams;

namespace TradeX.Trading.Backtest;

/// <summary>
/// Worker 端消费 Redis Stream <see cref="BacktestChannels.Tasks"/>，收到通知后把 taskId 注入本地 Channel
/// 唤醒 BacktestScheduler。同时定时（默认 5 分钟）扫一遍 DB 中的 Pending 任务作为兜底，
/// 保证即使 Stream 消息丢失/损坏也能最终一致。
/// </summary>
public sealed class BacktestTaskListener(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IBacktestTaskQueue queue,
    ILogger<BacktestTaskListener> logger) : BackgroundService
{
    private const string ConsumerGroup = "worker-backtest";
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SafetyNetInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();
        var streamKey = BacktestChannels.Tasks;
        var consumer = RedisStreamHelpers.DefaultConsumerName();

        await RedisStreamHelpers.EnsureConsumerGroupAsync(db, streamKey, ConsumerGroup, logger);
        logger.LogInformation("BacktestTaskListener 启动: stream={Stream} group={Group} consumer={Consumer}",
            streamKey, ConsumerGroup, consumer);

        // 启动后兜底扫描一次，捞回 Worker 离线期间累积的 Pending
        await SafetyNetDrainAsync(stoppingToken);

        // 先处理 PEL 残留
        await ConsumePelAsync(db, streamKey, consumer, stoppingToken);

        // 并发：长轮询 stream + 定时兜底扫表
        var safetyNet = Task.Run(() => SafetyNetLoopAsync(stoppingToken), stoppingToken);
        var streamLoop = Task.Run(() => StreamReadLoopAsync(db, streamKey, consumer, stoppingToken), stoppingToken);
        await Task.WhenAll(safetyNet, streamLoop);
        logger.LogInformation("BacktestTaskListener 已停止");
    }

    private async Task StreamReadLoopAsync(IDatabase db, string streamKey, string consumer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(streamKey, ConsumerGroup, consumer, ">", count: 20);
                if (entries.Length == 0)
                {
                    try { await Task.Delay(PollDelay, ct); }
                    catch (TaskCanceledException) { break; }
                    continue;
                }
                foreach (var entry in entries)
                {
                    if (ct.IsCancellationRequested) break;
                    await ProcessEntryAsync(db, streamKey, entry, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "BacktestTaskListener 读取异常，1s 后重试");
                try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
                catch (TaskCanceledException) { break; }
            }
        }
    }

    private async Task ConsumePelAsync(IDatabase db, string streamKey, string consumer, CancellationToken ct)
    {
        try
        {
            var pending = await db.StreamReadGroupAsync(streamKey, ConsumerGroup, consumer, "0", count: 100);
            if (pending.Length == 0) return;
            logger.LogInformation("BacktestTaskListener PEL 清理 {Count} 条", pending.Length);
            foreach (var entry in pending)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessEntryAsync(db, streamKey, entry, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BacktestTaskListener PEL 清理失败");
        }
    }

    private async Task ProcessEntryAsync(IDatabase db, string streamKey, StreamEntry entry, CancellationToken ct)
    {
        try
        {
            var raw = entry[RedisStreamHelpers.PayloadField];
            if (raw.HasValue && Guid.TryParseExact((string)raw!, "N", out var taskId))
            {
                await queue.EnqueueAsync(taskId, ct);
                logger.LogDebug("BacktestTaskListener 入队 TaskId={TaskId}", taskId);
            }
            else
            {
                logger.LogWarning("BacktestTaskListener 收到无效 payload id={Id}, ACK 跳过", entry.Id);
            }
            await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entry.Id);
        }
        catch (Exception ex)
        {
            // 不 ACK → 留待重试
            logger.LogError(ex, "BacktestTaskListener 处理失败 id={Id}, 留待重试", entry.Id);
        }
    }

    private async Task SafetyNetLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(SafetyNetInterval, ct); }
            catch (TaskCanceledException) { break; }
            await SafetyNetDrainAsync(ct);
        }
    }

    private async Task SafetyNetDrainAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
            var pending = await repo.GetByStatusAsync(BacktestTaskStatus.Pending, ct);
            if (pending.Count == 0) return;
            foreach (var task in pending)
                await queue.EnqueueAsync(task.Id, ct);
            logger.LogInformation("BacktestTaskListener 兜底扫描入队 {Count} 个 Pending 任务", pending.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BacktestTaskListener 兜底扫描异常");
        }
    }
}
