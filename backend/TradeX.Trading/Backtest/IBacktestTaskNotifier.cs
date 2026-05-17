using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Trading.Streams;

namespace TradeX.Trading.Backtest;

/// <summary>
/// 跨进程回测任务通知。API 端调 <see cref="NotifyTaskQueuedAsync"/> 后 Worker 端
/// <c>BacktestTaskListener</c> 收到通知并把 taskId 放入本地 Channel，唤醒 BacktestScheduler。
/// DB 是真相：如果通知丢失，Worker 启动时的 RecoverPendingTasks 会扫表兜底。
/// </summary>
public interface IBacktestTaskNotifier
{
    Task NotifyTaskQueuedAsync(Guid taskId, CancellationToken ct = default);
}

public static class BacktestChannels
{
    public const string Tasks = "tradex:backtest";
}

public sealed class RedisBacktestTaskNotifier(
    IConnectionMultiplexer redis,
    ILogger<RedisBacktestTaskNotifier> logger) : IBacktestTaskNotifier
{
    public async Task NotifyTaskQueuedAsync(Guid taskId, CancellationToken ct = default)
    {
        try
        {
            await RedisStreamHelpers.AddAsync(redis.GetDatabase(), BacktestChannels.Tasks, taskId.ToString("N"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis 回测任务通知发布失败 TaskId={TaskId}（Worker 会通过 DB 兜底）", taskId);
        }
    }
}

/// <summary>Redis 未配置时的降级实现 —— 仅本地进程的 Channel 工作；跨进程派发等启用 Redis 后恢复。</summary>
public sealed class NullBacktestTaskNotifier(ILogger<NullBacktestTaskNotifier> logger) : IBacktestTaskNotifier
{
    public Task NotifyTaskQueuedAsync(Guid taskId, CancellationToken ct = default)
    {
        logger.LogDebug("回测任务跨进程通知未启用（Redis 未配置）TaskId={TaskId}", taskId);
        return Task.CompletedTask;
    }
}
