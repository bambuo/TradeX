using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Events;
using TradeX.Trading.Streams;

namespace TradeX.Trading.Outbox;

/// <summary>
/// 后台轮询 outbox_events 表，将 Pending 事件发布到 Redis tradex:events 频道。
/// 解决"业务写 DB 已提交但 Redis 发布失败 → 事件永远丢失"的一致性漏洞。
///
/// 配合 <c>OutboxTradingEventBus</c>：业务路径写 outbox 行，本 relay 异步 XADD 到
/// tradex:events Stream，消费组（如 RedisToSignalRBridge）订阅后处理。
///
/// 设计点：
/// - 轮询间隔 2 秒（事件延迟可接受 &lt;5s）
/// - 失败重试最多 5 次，超过置 Failed 状态并上报 OutboxEventsFailed 指标供告警
/// - 仅在 Worker 进程运行，而 Worker 由 <c>WorkerSingleInstanceGuard</c> 强制单实例，
///   故不存在多 relay 重复 XADD；消费端另有 entryId 去重作二次防护
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
        var db = redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainBatchAsync(db, stoppingToken);
                if (processed == 0)
                {
                    try { await Task.Delay(PollInterval, stoppingToken); }
                    catch (TaskCanceledException) { break; }
                }
                // 有数据时立即下一轮，提升吞吐
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

    private async Task<int> DrainBatchAsync(IDatabase db, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var batch = await repo.PickPendingAsync(BatchSize, ct);
        if (batch.Count == 0) return 0;

        foreach (var evt in batch)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // outbox 里存的就是完整 envelope JSON，XADD 到 stream
                await RedisStreamHelpers.AddAsync(db, TradingEventChannels.Events, evt.PayloadJson);
                await repo.MarkSentAsync(evt.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox 事件发布失败 Id={Id} Type={Type} Attempt={Attempt}",
                    evt.Id, evt.Type, evt.AttemptCount + 1);
                var dead = await repo.MarkFailedAsync(evt.Id, ex.Message, MaxAttempts, ct);
                if (dead)
                {
                    metrics.OutboxEventsFailed.Add(1, new KeyValuePair<string, object?>("type", evt.Type));
                    logger.LogError("Outbox 事件重试耗尽进入 Failed 终态（毒消息）Id={Id} Type={Type}", evt.Id, evt.Type);
                }
            }
        }
        return batch.Count;
    }
}
