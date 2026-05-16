using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Backtest;

/// <summary>
/// Worker 端订阅 <see cref="BacktestChannels.Tasks"/>，收到通知后把 taskId 注入本地 Channel
/// 唤醒 BacktestScheduler。同时定时（默认 5 分钟）扫一遍 DB 中的 Pending 任务作为兜底，
/// 保证即使 Pub/Sub 消息丢失也能最终一致。
/// </summary>
public sealed class BacktestTaskListener(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IBacktestTaskQueue queue,
    ILogger<BacktestTaskListener> logger) : BackgroundService
{
    private static readonly TimeSpan SafetyNetInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Literal(BacktestChannels.Tasks);
        var rq = await subscriber.SubscribeAsync(channel);
        rq.OnMessage(msg => _ = HandleAsync(msg.Message, stoppingToken));
        logger.LogInformation("BacktestTaskListener 订阅: {Channel}", BacktestChannels.Tasks);

        // 启动后立刻做一次兜底扫描，把 Worker 离线期间累积的 Pending 任务捞回队列
        await SafetyNetDrainAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(SafetyNetInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
                await SafetyNetDrainAsync(stoppingToken);
            }
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
            logger.LogInformation("BacktestTaskListener 已停止");
        }
    }

    private async Task HandleAsync(RedisValue raw, CancellationToken ct)
    {
        if (!raw.HasValue) return;
        if (!Guid.TryParseExact((string)raw!, "N", out var taskId)) return;
        try
        {
            await queue.EnqueueAsync(taskId, ct);
            logger.LogDebug("BacktestTaskListener 入队 TaskId={TaskId}", taskId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BacktestTaskListener 入队异常 TaskId={TaskId}", taskId);
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
