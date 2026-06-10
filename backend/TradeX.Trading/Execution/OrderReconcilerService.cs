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

        // 启动时一次性扫描"孤儿订单"(交易所有 / 本地无). 通过 IDomainEventBus 异步告警.
        try
        {
            using var scope = scopeFactory.CreateScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<IOrderReconciler>();
            var orphans = await reconciler.DetectOrphanOrdersAsync(stoppingToken);
            if (orphans > 0)
                logger.LogWarning("启动孤儿订单扫描: 发现 {Count} 笔, 已写入事件总线告警", orphans);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动孤儿订单扫描异常, 跳过");
        }

        // 持仓级对账独立间隔（通常比订单对账更稀疏），首轮即触发一次。
        var positionInterval = TimeSpan.FromSeconds(Math.Max(30, riskSettings.Value.PositionReconcileIntervalSeconds));
        var lastPositionRunAt = DateTime.MinValue;

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

                // 持仓级对账按自身间隔触发（复用同一 scope）
                if (DateTime.UtcNow - lastPositionRunAt >= positionInterval)
                {
                    var positionReconciler = scope.ServiceProvider.GetRequiredService<IPositionReconciler>();
                    await positionReconciler.ReconcilePositionsAsync(stoppingToken);
                    lastPositionRunAt = DateTime.UtcNow;
                }
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
