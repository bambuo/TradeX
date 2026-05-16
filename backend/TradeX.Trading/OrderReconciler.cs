using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

/// <summary>
/// 订单对账器：周期性扫描 Pending 订单，配合 TradeExecutor 三段式实现崩溃恢复。
///
/// 处理矩阵：
/// <list type="bullet">
///   <item><b>有 ExchangeOrderId</b>：调用交易所 <c>GetOrderAsync</c> 刷新状态；
///         成交→Filled，部分成交→PartiallyFilled，查不到且超过陈旧阈值→Failed。</item>
///   <item><b>无 ExchangeOrderId</b>（pre-persist 后崩溃 / 调用交易所抛异常）：
///         超过陈旧阈值即标记 Failed。未来 5 家客户端补全 ClientOrderId 透传后，
///         可改为凭 ClientOrderId 反查交易所确认是否真的没下单。</item>
/// </list>
/// </summary>
public class OrderReconciler(
    IExchangeRepository exchangeRepo,
    IOrderRepository orderRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryption,
    IOptions<RiskSettings> riskSettings,
    ILogger<OrderReconciler> logger) : IOrderReconciler
{
    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var stalenessThreshold = TimeSpan.FromMinutes(Math.Max(1, riskSettings.Value.StalePendingMinutes));

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
            if (ct.IsCancellationRequested) break;

            var pending = await orderRepo.GetPendingByExchangeAsync(exchange.Id, ct);
            if (pending.Count == 0) continue;
            totalChecked += pending.Count;

            IExchangeClient? client = null;

            foreach (var order in pending)
            {
                if (ct.IsCancellationRequested) break;
                if (order.Status != OrderStatus.Pending) continue;

                var age = DateTime.UtcNow - order.PlacedAtUtc;

                if (string.IsNullOrEmpty(order.ExchangeOrderId))
                {
                    // 没有 ExchangeOrderId —— 凭 ClientOrderId 反查（若客户端支持）
                    client ??= TryCreateClient(exchange);
                    if (client is not null)
                    {
                        try
                        {
                            var lookup = await client.GetOrderByClientOrderIdAsync(order.Pair, order.ClientOrderId.ToString("N"), ct);
                            if (lookup.Success)
                            {
                                // 交易所确实收到了该订单 —— 回填 ExchangeOrderId 并按返回状态更新
                                order.ExchangeOrderId = lookup.ExchangeOrderId;
                                var changed = ApplyResultToOrder(order, lookup);
                                await orderRepo.UpdateAsync(order, ct);
                                totalFixed++;
                                logger.LogInformation("Reconciliation 凭 ClientOrderId 恢复订单: OrderId={OrderId}, ClientOrderId={Cli}, ExchangeOrderId={Exch}, Status={Status}",
                                    order.Id, order.ClientOrderId, order.ExchangeOrderId, order.Status);
                                continue;
                            }
                            // not_supported 或交易所明确说"找不到" —— 走超时判定
                            if (age >= stalenessThreshold)
                            {
                                await MarkFailedAsync(order, $"对账：交易所无此 ClientOrderId ({lookup.Error})", ct);
                                totalFixed++;
                            }
                            continue;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Reconciliation 按 ClientOrderId 反查异常, OrderId={OrderId}", order.Id);
                        }
                    }

                    if (age >= stalenessThreshold)
                    {
                        await MarkFailedAsync(order, "对账超时（无 ExchangeOrderId 且无法反查）", ct);
                        totalFixed++;
                    }
                    continue;
                }

                // 有 ExchangeOrderId —— 向交易所核实当前状态
                client ??= TryCreateClient(exchange);
                if (client is null)
                {
                    if (age >= stalenessThreshold * 2)
                    {
                        await MarkFailedAsync(order, "交易所客户端不可用，长时间无法核实", ct);
                        totalFixed++;
                    }
                    continue;
                }

                try
                {
                    var result = await client.GetOrderAsync(order.ExchangeOrderId, ct);
                    if (result.Success)
                    {
                        var updated = ApplyResultToOrder(order, result);
                        if (updated)
                        {
                            await orderRepo.UpdateAsync(order, ct);
                            totalFixed++;
                            logger.LogInformation("Reconciliation 修复订单: OrderId={OrderId}, ExchangeOrderId={Exch}, Status={Status}",
                                order.Id, order.ExchangeOrderId, order.Status);
                        }
                    }
                    else if (age >= stalenessThreshold)
                    {
                        await MarkFailedAsync(order, $"交易所查询失败: {result.Error}", ct);
                        totalFixed++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Reconciliation 查询交易所异常, OrderId={OrderId}, ExchangeOrderId={Exch}",
                        order.Id, order.ExchangeOrderId);
                }
            }
        }

        if (totalChecked > 0)
            logger.LogInformation("Reconciliation 完成: 检查 {CheckedCount} 笔, 修复 {FixedCount} 笔",
                totalChecked, totalFixed);
    }

    private IExchangeClient? TryCreateClient(TradeX.Core.Models.Exchange exchange)
    {
        try
        {
            var pass = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;
            return clientFactory.CreateClient(
                exchange.Type,
                encryption.Decrypt(exchange.ApiKeyEncrypted),
                encryption.Decrypt(exchange.SecretKeyEncrypted),
                pass);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reconciliation 创建交易所客户端失败, ExchangeId={ExchangeId}", exchange.Id);
            return null;
        }
    }

    private static bool ApplyResultToOrder(Order order, OrderResult result)
    {
        var prev = order.Status;
        order.FilledQuantity = result.FilledQuantity;
        order.Fee = result.Fee;
        if (result.FilledQuantity >= order.Quantity && order.Quantity > 0)
            order.Status = OrderStatus.Filled;
        else if (result.FilledQuantity > 0)
            order.Status = OrderStatus.PartiallyFilled;
        // 否则保持 Pending，下一轮再查
        order.UpdatedAt = DateTime.UtcNow;
        return order.Status != prev;
    }

    private async Task MarkFailedAsync(Order order, string reason, CancellationToken ct)
    {
        order.Status = OrderStatus.Failed;
        order.UpdatedAt = DateTime.UtcNow;
        await orderRepo.UpdateAsync(order, ct);
        logger.LogWarning("Reconciliation 标记订单失败: OrderId={OrderId}, Reason={Reason}", order.Id, reason);
    }
}
