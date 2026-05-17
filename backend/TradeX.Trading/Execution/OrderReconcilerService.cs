using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Trading.Risk;

namespace TradeX.Trading.Execution;

/// <summary>
/// Worker 进程的 BackgroundService — 按 <see cref="RiskSettings.OrderReconcileIntervalSeconds"/>
/// 周期性调用 <see cref="IOrderReconciler.ReconcileAsync"/>。
/// 每轮从 DI 取一个新的 scope，确保 EF DbContext 正确释放。
/// </summary>
public sealed class OrderReconcilerService(
    IServiceScopeFactory scopeFactory,
    IOptions<RiskSettings> riskSettings,
    ILogger<OrderReconcilerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, riskSettings.Value.OrderReconcileIntervalSeconds));
        logger.LogInformation("OrderReconcilerService 启动，巡检周期 {Interval}", interval);

        // 启动后先等待一个周期，避免与首轮订单写入的竞争窗口
        try { await Task.Delay(interval, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<IOrderReconciler>();
                await reconciler.ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "OrderReconcilerService 周期执行异常，将于下一周期重试");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }

        logger.LogInformation("OrderReconcilerService 已停止");
    }
}
