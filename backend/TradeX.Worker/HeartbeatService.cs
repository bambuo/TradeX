using System.Diagnostics;

namespace TradeX.Worker;

/// <summary>
/// 阶段 1 占位 BackgroundService — 仅做心跳日志，验证 Worker 进程能正常启动、DI 工作、日志/OTel 通路打通。
/// 阶段 2 会从这里搬入 TradingEngine、BacktestScheduler、OrderReconciler 等真正的后台任务。
/// </summary>
public sealed class HeartbeatService(ILogger<HeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processName = Process.GetCurrentProcess().ProcessName;
        logger.LogInformation("Worker 启动: ProcessName={Name}, MachineName={Machine}, PID={Pid}",
            processName, Environment.MachineName, Environment.ProcessId);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Worker heartbeat at {Time:o}", DateTimeOffset.UtcNow);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Worker 正在关闭...");
    }
}
