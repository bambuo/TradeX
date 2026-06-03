using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Core.Interfaces;
using TradeX.Trading.Events;
using TradeX.Trading.Streams;

namespace TradeX.Trading.Outbox;

/// <summary>
/// 后台轮询 outbox_events 表，将 Pending 事件批量发布到 Redis tradex:events 频道。
///
/// 设计点：
/// - FOR UPDATE SKIP LOCKED 防止多实例竞态（配合 WorkerSingleInstanceGuard 双重防护）
/// - 每批 50 行先全部 XADD，成功后批量 UPDATE Status=Sent，将 N+1 事务降为 1 个
/// - 失败重试最多 5 次，超过置 Failed 状态
/// </summary>
public sealed class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IConnectionMultiplexer redis,
    TradeX.Trading.Observability.TradeXMetrics metrics,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxRelayService 启动，轮询间隔 {Interval}, 目标 stream {Stream}",
            PollInterval, TradingEventChannels.Events);
        var redisDb = redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainBatchAsync(redisDb, stoppingToken);
                if (processed == 0)
                {
                    try { await Task.Delay(PollInterval, stoppingToken); }
                    catch (TaskCanceledException) { break; }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxRelay 轮询异常，等待重试");
                try { await Task.Delay(PollInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
        logger.LogInformation("OutboxRelayService 已停止");
    }

    private async Task<int> DrainBatchAsync(IDatabase redisDb, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var batch = await repo.PickPendingAsync(BatchSize, ct);
        if (batch.Count == 0) return 0;

        // 第一轮：尝试 XADD，收集成功/失败的 Id
        var sentIds = new List<Guid>(batch.Count);
        var failedIds = new List<(Guid Id, string Error, int Attempt)>(batch.Count);

        foreach (var evt in batch)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await RedisStreamHelpers.AddAsync(redisDb, TradingEventChannels.Events, evt.PayloadJson);
                sentIds.Add(evt.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox 事件发布失败 Id={Id} Type={Type} Attempt={Attempt}",
                    evt.Id, evt.Type, evt.AttemptCount + 1);
                failedIds.Add((evt.Id, ex.Message, evt.AttemptCount));
            }
        }

        // 第二轮：批量标记已发送（1 个事务替代 N 个）
        if (sentIds.Count > 0)
            await repo.MarkSentBatchAsync(sentIds, ct);

        // 第三轮：逐行处理失败（含重试逻辑）
        foreach (var (id, error, attempt) in failedIds)
        {
            var dead = await repo.MarkFailedAsync(id, error, MaxAttempts, ct);
            if (dead)
            {
                metrics.OutboxEventsFailed.Add(1);
                logger.LogError("Outbox 事件重试耗尽进入 Failed 终态（毒消息）Id={Id}", id);
            }
        }

        return batch.Count;
    }
}
