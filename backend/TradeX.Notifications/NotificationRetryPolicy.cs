using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;

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
/// 通知发送的重试策略.
///   * 重试: 指数退避 (200ms / 400ms / 800ms), 最多 3 次
///   * 全部失败后: 记录日志, 不再写入数据库 DLQ
///   * 不重试的异常: ArgumentException / InvalidOperationException (业务级配置错误, 重试也没用)
/// 该类是纯协调器, 不耦合具体 channel 实现; 调用方传入一个执行委托.
/// </summary>
public sealed class NotificationRetryPolicy(INotificationMetrics metrics, ILogger<NotificationRetryPolicy> logger)
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
            catch (ArgumentException ex) { lastEx = ex; break; }
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

        metrics.RecordFailed(channel);
        logger.LogError(lastEx, "通知最终失败 (重试耗尽): Channel={Channel}, Type={Type}", channel, @event.Type);
        return false;
    }
}
