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
/// 与 <c>RedisEventBus</c> 配合：业务路径调用 <c>OutboxTradingEventBus</c>（写 outbox），
/// 后台 relay 异步把 outbox 内容 publish 到 Redis；Redis 消费侧不变。
///
/// 设计点：
/// - 轮询间隔 2 秒（事件延迟可接受 &lt;5s；前端 SignalR 实时性要求由 Redis Pub/Sub 满足）
/// - 失败重试最多 5 次，超过置 Failed 状态人工排查
/// - 单实例运行；多实例同时跑无大碍（每行被多次发布，订阅端去重靠业务）
/// </summary>
public sealed class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IConnectionMultiplexer redis,
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
                await repo.MarkFailedAsync(evt.Id, ex.Message, MaxAttempts, ct);
            }
        }
        return batch.Count;
    }
}
