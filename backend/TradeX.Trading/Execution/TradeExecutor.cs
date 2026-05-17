using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Execution;

/// <summary>
/// 三段式下单：
///   1. <b>pre-persist</b> — 在调用交易所之前写入 Order(Pending) + ClientOrderId 幂等键。
///   2. <b>call exchange</b> — 向交易所提交订单，ClientOrderId 一并透传（由各 IExchangeClient 实现选择是否使用）。
///   3. <b>post-update</b> — 根据交易所返回更新 Order 状态（Filled/Failed/PartiallyFilled）+ ExchangeOrderId/FilledQuantity/Fee。
///
/// 任意阶段进程崩溃后，DB 中的 Pending 订单可被 <c>OrderReconciler</c> 凭 ClientOrderId 反查交易所恢复。
/// </summary>
public class TradeExecutor(
    IExchangeClientFactory exchangeFactory,
    IExchangeRepository exchangeRepo,
    IOrderRepository orderRepo,
    IEncryptionService encryptionService,
    ILogger<TradeExecutor> logger) : ITradeExecutor
{
    public Task<OrderResult> ExecuteMarketOrderAsync(Order order, CancellationToken ct = default)
        => ExecuteOrderAsync(order, OrderType.Market, null, null, ct);

    public Task<OrderResult> ExecuteLimitOrderAsync(Order order, CancellationToken ct = default)
    {
        if (order.Price is null)
            return Task.FromResult(new OrderResult(false, null, 0, 0, 0, "限价单必须指定价格"));
        return ExecuteOrderAsync(order, OrderType.Limit, order.Price, null, ct);
    }

    public Task<OrderResult> ExecuteStopLimitOrderAsync(Order order, decimal stopPrice, CancellationToken ct = default)
    {
        if (order.Price is null)
            return Task.FromResult(new OrderResult(false, null, 0, 0, 0, "止损限价单必须指定价格"));
        return ExecuteOrderAsync(order, OrderType.StopLimit, order.Price, stopPrice, ct);
    }

    private async Task<OrderResult> ExecuteOrderAsync(Order order, OrderType orderType, decimal? price, decimal? stopPrice, CancellationToken ct)
    {
        order.Type = orderType;
        order.Status = OrderStatus.Pending;
        order.UpdatedAt = DateTime.UtcNow;

        // ---- 阶段 1: pre-persist ----
        // 在调用交易所之前把订单写入 DB，使后续崩溃可凭 ClientOrderId 恢复。
        try
        {
            await orderRepo.AddAsync(order, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "订单入库失败（pre-persist），中止下单, OrderId={OrderId}, ClientOrderId={ClientOrderId}",
                order.Id, order.ClientOrderId);
            return new OrderResult(false, null, 0, 0, 0, $"订单入库失败: {ex.Message}");
        }

        // ---- 阶段 2: call exchange ----
        OrderResult exchangeResult;
        try
        {
            var exchange = await exchangeRepo.GetByIdAsync(order.ExchangeId, ct);
            if (exchange is null)
            {
                await MarkFailedAsync(order, "交易所不存在", ct);
                return new OrderResult(false, null, 0, 0, 0, "交易所不存在");
            }

            var client = exchangeFactory.CreateClient(
                exchange.Type,
                encryptionService.Decrypt(exchange.ApiKeyEncrypted),
                encryptionService.Decrypt(exchange.SecretKeyEncrypted),
                exchange.PassphraseEncrypted is not null ? encryptionService.Decrypt(exchange.PassphraseEncrypted) : null);

            var quantity = order.Side == OrderSide.Buy ? order.QuoteQuantity : order.Quantity;

            logger.LogInformation("执行{OrderSide}{OrderType}单: {Pair} {Quantity} @ {Price}, OrderId={OrderId}, ClientOrderId={ClientOrderId}",
                order.Side, orderType, order.Pair, quantity, price, order.Id, order.ClientOrderId);

            exchangeResult = await client.PlaceOrderAsync(
                new OrderRequest(order.Pair, order.Side, orderType, quantity, price, stopPrice, order.ClientOrderId.ToString("N")),
                ct);
        }
        catch (Exception ex)
        {
            // 注意：不能确定交易所是否实际收到/成交，订单标记 Pending 等待对账，而非 Failed
            logger.LogError(ex, "下单调用交易所异常，订单保留 Pending 等待对账, OrderId={OrderId}", order.Id);
            return new OrderResult(false, null, 0, 0, 0, $"下单异常: {ex.Message}");
        }

        // ---- 阶段 3: post-update ----
        try
        {
            await ApplyExchangeResultAsync(order, exchangeResult, ct);
        }
        catch (Exception ex)
        {
            // 交易所已成交但本地状态写不进去 —— 严重不一致，但订单凭 ExchangeOrderId/ClientOrderId 可被对账器恢复
            logger.LogError(ex, "订单 post-update 失败, 交易所已返回但 DB 更新失败, OrderId={OrderId}, ExchangeOrderId={ExchangeOrderId}",
                order.Id, exchangeResult.ExchangeOrderId);
        }

        return exchangeResult;
    }

    private async Task ApplyExchangeResultAsync(Order order, OrderResult result, CancellationToken ct)
    {
        order.UpdatedAt = DateTime.UtcNow;
        if (result.Success)
        {
            order.ExchangeOrderId = result.ExchangeOrderId;
            order.FilledQuantity = result.FilledQuantity;
            order.Fee = result.Fee;
            order.Status = result.FilledQuantity >= order.Quantity
                ? OrderStatus.Filled
                : (result.FilledQuantity > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Pending);
        }
        else
        {
            order.Status = OrderStatus.Failed;
        }
        await orderRepo.UpdateAsync(order, ct);
    }

    private async Task MarkFailedAsync(Order order, string reason, CancellationToken ct)
    {
        order.Status = OrderStatus.Failed;
        order.UpdatedAt = DateTime.UtcNow;
        try
        {
            await orderRepo.UpdateAsync(order, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "标记订单失败时写库异常, OrderId={OrderId}, Reason={Reason}", order.Id, reason);
        }
    }
}
