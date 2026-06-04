using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Risk;

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
    OrderBookSlippageGuard slippageGuard,
    PairRuleCache pairRuleCache,
    IFillProjector fillProjector,
    IOptions<RiskSettings> riskSettings,
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

            // 统一换算为基础货币数量（市价买单按订单簿/限价买单按委托价把 QuoteQuantity → base）
            // 并对市价单执行订单簿滑点护栏。失败则拒单（此前已 pre-persist，标 Failed）。
            var (ok, baseQuantity, prepError) = await PrepareQuantityAsync(client, order, orderType, price, ct);
            if (!ok)
            {
                logger.LogWarning("下单前置检查未通过，拒单: OrderId={OrderId}, Reason={Reason}", order.Id, prepError);
                await MarkFailedAsync(order, prepError ?? "下单前置检查未通过", ct);
                return new OrderResult(false, null, 0, 0, 0, prepError);
            }

            // 写回申报数量，使 RecordFill 能据 (FilledQuantity >= Quantity) 正确判定 Filled
            order.Quantity = baseQuantity;

            logger.LogInformation("执行{OrderSide}{OrderType}单: {Pair} {Quantity} @ {Price}, OrderId={OrderId}, ClientOrderId={ClientOrderId}",
                order.Side, orderType, order.Pair, baseQuantity, price, order.Id, order.ClientOrderId);

            exchangeResult = await client.PlaceOrderAsync(
                new OrderRequest(order.Pair, order.Side, orderType, baseQuantity, price, stopPrice, order.ClientOrderId.ToString("N")),
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

    /// <summary>
    /// 计算实际提交给交易所的基础货币数量，并对市价单做订单簿滑点护栏。
    /// 买单：QuoteQuantity（quote 金额）→ base（市价用最优卖价，限价用委托价）。
    /// 卖单：直接用 Quantity（已是 base）。
    /// </summary>
    private async Task<(bool Ok, decimal Quantity, string? Error)> PrepareQuantityAsync(
        IExchangeClient client, Order order, OrderType orderType, decimal? price, CancellationToken ct)
    {
        OrderBook? book = null;
        if (orderType == OrderType.Market)
        {
            try { book = await client.GetOrderBookAsync(order.Pair, 50, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "获取订单簿失败, OrderId={OrderId}", order.Id); }
        }

        // 参考价：市价取最优对手价，限价取委托价。用于 quote→base 换算、名义价值校验、滑点估算。
        var refPrice = orderType == OrderType.Market
            ? (book is null ? 0m : BestPrice(order.Side == OrderSide.Buy ? book.Asks : book.Bids))
            : price ?? 0m;

        decimal baseQuantity;
        if (order.Side == OrderSide.Sell)
        {
            if (order.Quantity <= 0) return (false, 0, "卖单数量无效");
            baseQuantity = order.Quantity;
        }
        else
        {
            var quote = order.QuoteQuantity;
            if (quote <= 0)
                return order.Quantity > 0 ? (true, order.Quantity, null) : (false, 0, "买单金额无效");
            if (refPrice <= 0)
                return (false, 0, orderType == OrderType.Market ? "无法获取订单簿参考价，拒绝市价买单" : "限价买单缺少有效价格");
            baseQuantity = quote / refPrice;
            if (baseQuantity <= 0) return (false, 0, "换算后数量无效");
        }

        // 按交易对规则取整数量 + 校验最小下单量/名义价值（规则缺失则降级跳过，不阻断交易）
        PairRule? rule = null;
        try { rule = await pairRuleCache.GetRuleAsync(order.ExchangeId, client, order.Pair, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "获取交易对规则失败, Pair={Pair}", order.Pair); }
        if (rule is not null)
        {
            if (rule.StepSize > 0)
            {
                var rounded = Math.Floor(baseQuantity / rule.StepSize) * rule.StepSize;
                if (rounded <= 0)
                    return (false, 0, $"数量按步进 {rule.StepSize} 取整后为 0（原始 {baseQuantity}）");
                baseQuantity = rounded;
            }
            if (rule.MinQuantity > 0 && baseQuantity < rule.MinQuantity)
                return (false, 0, $"数量 {baseQuantity} 低于最小下单量 {rule.MinQuantity}");
            if (rule.MinNotional > 0 && refPrice > 0 && baseQuantity * refPrice < rule.MinNotional)
                return (false, 0, $"名义价值 {baseQuantity * refPrice:F2} 低于最小下单额 {rule.MinNotional}");
        }

        // 市价单滑点护栏（走订单簿模拟成交）
        if (orderType == OrderType.Market)
        {
            var maxPct = riskSettings.Value.MaxSlippagePercent;
            if (maxPct > 0 && book is not null && refPrice > 0)
            {
                var est = slippageGuard.Estimate(book, order.Side, baseQuantity, refPrice);
                if (!est.Sufficient)
                    return (false, 0, $"订单簿深度不足，拒绝市价单: {est.Reason}");
                if (est.SlippagePercent > maxPct)
                    return (false, 0, $"预估滑点 {est.SlippagePercent:F2}% 超过上限 {maxPct}%");
            }
        }

        return (true, baseQuantity, null);
    }

    private static decimal BestPrice(decimal[,] ladder)
        => ladder.GetLength(0) > 0 ? ladder[0, 0] : 0m;

    private async Task ApplyExchangeResultAsync(Order order, OrderResult result, CancellationToken ct)
    {
        if (result.Success)
            order.RecordFill(result.FilledQuantity, result.Fee, result.ExchangeOrderId, result.FeeAsset);
        else
            order.MarkFailed(result.Error);
        await orderRepo.UpdateAsync(order, ct);

        // 成交 → 持仓投影（买开 / 卖平），幂等。投影失败不影响订单已落库的结果，
        // 后续对账器再次发现 Filled 时会重试投影。
        if (order.Status == OrderStatus.Filled)
        {
            try
            {
                await fillProjector.ProjectFilledAsync(order, result.AvgPrice, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "成交→持仓投影失败（订单已成交），OrderId={OrderId}", order.Id);
            }
        }
    }

    private async Task MarkFailedAsync(Order order, string reason, CancellationToken ct)
    {
        order.MarkFailed(reason);
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
