using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;

namespace TradeX.Trading;

public class OrderReconciler(
    IExchangeRepository exchangeRepo,
    IOrderRepository orderRepo,
    ILogger<OrderReconciler> logger) : IOrderReconciler
{
    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var enabledExchanges = await exchangeRepo.GetAllEnabledAsync(ct);
        if (enabledExchanges.Count == 0)
        {
            logger.LogDebug("Reconciliation: 无已启用交易所");
            return;
        }

        var totalChecked = 0;
        var totalFixed = 0;

        foreach (var exchange in enabledExchanges)
        {
            var pendingOrders = await orderRepo.GetPendingByExchangeAsync(exchange.Id, ct);
            totalChecked += pendingOrders.Count;

            foreach (var order in pendingOrders)
            {
                if (order.Status == Core.Enums.OrderStatus.Pending && order.PlacedAtUtc < DateTime.UtcNow.AddMinutes(-5))
                {
                    order.Status = Core.Enums.OrderStatus.Failed;
                    order.UpdatedAt = DateTime.UtcNow;
                    await orderRepo.UpdateAsync(order, ct);
                    totalFixed++;
                    logger.LogWarning("Reconciliation: 订单超时标记为失败, OrderId={OrderId}, ExchangeId={ExchangeId}",
                        order.Id, exchange.Id);
                }
            }
        }

        logger.LogInformation("Reconciliation 完成: 检查 {CheckedCount} 笔订单, 修复 {FixedCount} 笔",
            totalChecked, totalFixed);
    }
}
