using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Notifications;

/// <summary>
/// 与 TradeX.Trading.Observability.TradeXMetrics 解耦的最小指标接口, 避免 Notifications 模块反向依赖 Trading 模块.
/// 由 Worker/Api Program 注入实际实现 (内部转发到 TradeXMetrics.NotificationsFailed).
/// </summary>
public interface INotificationMetrics
{
    void RecordFailed(string channel);
}

public sealed class NullNotificationMetrics : INotificationMetrics
{
    public void RecordFailed(string channel) { }
}

/// <summary>
/// 通知发送的重试策略 + 死信兜底.
///   * 重试: 指数退避 (200ms / 400ms / 800ms), 最多 3 次
///   * 全部失败后: 写一条 OutboxEvent("NotificationFailed") 作为 DLQ, 运营从 outbox_events 表能查到死信
///   * 不重试的异常: ArgumentException / InvalidOperationException (业务级配置错误, 重试也没用)
/// 该类是纯协调器, 不耦合具体 channel 实现; 调用方传入一个执行委托.
/// </summary>
public sealed class NotificationRetryPolicy(IOutboxRepository outbox, INotificationMetrics metrics, ILogger<NotificationRetryPolicy> logger)
{
    public const int MaxAttempts = 3;

    public async Task<bool> ExecuteAsync(string channel, NotificationEvent @event, Func<CancellationToken, Task> send, CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(200);
        Exception? lastEx = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await send(ct);
                if (attempt > 1)
                    logger.LogInformation("通知发送重试成功: Channel={Channel}, Attempt={Attempt}, Type={Type}",
                        channel, attempt, @event.Type);
                return true;
            }
            catch (ArgumentException ex) { lastEx = ex; break; }       // 配置类错误, 不重试
            catch (InvalidOperationException ex) { lastEx = ex; break; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastEx = ex;
                logger.LogWarning(ex, "通知发送失败 (重试 {Attempt}/{Max}): Channel={Channel}, Type={Type}",
                    attempt, MaxAttempts, channel, @event.Type);
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(delay, ct);
                    delay *= 2;
                }
            }
        }

        await EnqueueDeadLetterAsync(channel, @event, lastEx, ct);
        return false;
    }

    private async Task EnqueueDeadLetterAsync(string channel, NotificationEvent @event, Exception? lastEx, CancellationToken ct)
    {
        try
        {
            await outbox.EnqueueAsync(new OutboxEvent
            {
                Type = "NotificationFailed",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    channel,
                    eventType = @event.Type,
                    strategyName = @event.StrategyName,
                    data = @event.Data,
                    lastError = lastEx?.Message,
                    failedAtUtc = DateTime.UtcNow
                }),
                TraderId = null
            }, ct);
            metrics.RecordFailed(channel);
            logger.LogError(lastEx, "通知最终失败, 已写入 DLQ: Channel={Channel}, Type={Type}", channel, @event.Type);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "通知 DLQ 写入失败 (双重失败): Channel={Channel}, Type={Type}", channel, @event.Type);
        }
    }
}
