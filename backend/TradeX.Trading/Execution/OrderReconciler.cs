using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Risk;

namespace TradeX.Trading.Execution;

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
///
/// 性能说明（N+1 SaveChanges）：
/// ReconcileAsync 中每个 UpdateAsync 前面都依赖一次独立的交易所 API 调用
/// （GetOrderByClientOrderIdAsync / GetOrderAsync），因此无法合并为批量 SaveChanges。
/// 这是业务语义决定的——必须先查询外部系统再决定本地状态，而非批量更新模式。
/// </summary>
public class OrderReconciler(
    IExchangeRepository exchangeRepo,
    IOrderRepository orderRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryption,
    IOutboxRepository outbox,
    IFillProjector fillProjector,
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
                                await MaybeProjectFillAsync(order, lookup, ct);
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
                    var result = await client.GetOrderAsync(order.Pair, order.ExchangeOrderId, ct);
                    if (result.Success)
                    {
                        var updated = ApplyResultToOrder(order, result);
                        if (updated)
                        {
                            await orderRepo.UpdateAsync(order, ct);
                            await MaybeProjectFillAsync(order, result, ct);
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

    public async Task<int> DetectOrphanOrdersAsync(CancellationToken ct = default)
    {
        var enabledExchanges = await exchangeRepo.GetAllEnabledAsync(ct);
        if (enabledExchanges.Count == 0) return 0;

        var totalOrphans = 0;

        foreach (var exchange in enabledExchanges)
        {
            if (ct.IsCancellationRequested) break;
            var client = TryCreateClient(exchange);
            if (client is null) continue;

            ExchangeOrderDto[] openOrders;
            try
            {
                openOrders = await client.GetOpenOrdersAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "孤儿检测: 拉取未结订单失败, ExchangeId={ExchangeId}", exchange.Id);
                continue;
            }

            foreach (var remote in openOrders)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(remote.ExchangeOrderId)) continue;

                var local = await orderRepo.GetByExchangeOrderIdAsync(remote.ExchangeOrderId, ct);
                if (local is not null) continue;

                totalOrphans++;
                // 包装为标准 TradingEventEnvelope，使 RedisToSignalRBridge 能解析并路由到管理员组。
                // 孤儿订单无 trader 归属，TraderId 用 Guid.Empty。
                var data = new TradeX.Trading.Events.OrphanOrderDetectedPayload(
                    exchange.Id, exchange.Type.ToString(), remote.Pair, remote.ExchangeOrderId,
                    remote.Side, remote.Type, remote.Price, remote.Quantity, DateTime.UtcNow);
                var envelope = new TradeX.Trading.Events.TradingEventEnvelope(
                    TradeX.Trading.Events.TradingEventTypes.OrphanOrderDetected,
                    Guid.NewGuid(), Guid.Empty,
                    System.Text.Json.JsonSerializer.Serialize(data));
                await outbox.EnqueueAsync(new OutboxEvent
                {
                    Type = TradeX.Trading.Events.TradingEventTypes.OrphanOrderDetected,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(envelope),
                    TraderId = null
                }, ct);
                await outbox.SaveChangesAsync(ct);
                logger.LogWarning("孤儿订单检测: ExchangeId={ExchangeId}, Pair={Pair}, ExchangeOrderId={Eid}",
                    exchange.Id, remote.Pair, remote.ExchangeOrderId);
            }
        }

        if (totalOrphans > 0)
            logger.LogWarning("孤儿订单巡检完成: 共发现 {Count} 笔交易所未结订单本地缺失记录", totalOrphans);
        return totalOrphans;
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

    /// <summary>对账中订单转为 Filled 时，触发幂等的"成交→持仓"投影（崩溃恢复路径）。</summary>
    private async Task MaybeProjectFillAsync(Order order, OrderResult result, CancellationToken ct)
    {
        if (order.Status != OrderStatus.Filled) return;
        try
        {
            await fillProjector.ProjectFilledAsync(order, result.AvgPrice, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "对账：成交→持仓投影失败，OrderId={OrderId}", order.Id);
        }
    }

    private static bool ApplyResultToOrder(Order order, OrderResult result)
    {
        if (order.IsTerminal()) return false;
        var prev = order.Status;
        order.RecordFill(result.FilledQuantity, result.Fee, feeAsset: result.FeeAsset);
        return order.Status != prev;
    }

    private async Task MarkFailedAsync(Order order, string reason, CancellationToken ct)
    {
        if (order.IsTerminal()) return;
        order.MarkFailed(reason);
        await orderRepo.UpdateAsync(order, ct);
        logger.LogWarning("Reconciliation 标记订单失败: OrderId={OrderId}, Reason={Reason}", order.Id, reason);
    }
}
