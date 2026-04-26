using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class TradeExecutor(
    IExchangeClientFactory exchangeFactory,
    IExchangeRepository exchangeRepo,
    IEncryptionService encryptionService,
    ILogger<TradeExecutor> logger) : ITradeExecutor
{
    public async Task<OrderResult> ExecuteMarketOrderAsync(Order order, CancellationToken ct = default)
    {
        return await ExecuteOrderAsync(order, OrderType.Market, null, null, ct);
    }

    public async Task<OrderResult> ExecuteLimitOrderAsync(Order order, CancellationToken ct = default)
    {
        if (order.Price is null)
            return new OrderResult(false, null, 0, 0, 0, "限价单必须指定价格");

        return await ExecuteOrderAsync(order, OrderType.Limit, order.Price, null, ct);
    }

    public async Task<OrderResult> ExecuteStopLimitOrderAsync(Order order, decimal stopPrice, CancellationToken ct = default)
    {
        if (order.Price is null)
            return new OrderResult(false, null, 0, 0, 0, "止损限价单必须指定价格");

        return await ExecuteOrderAsync(order, OrderType.StopLimit, order.Price, stopPrice, ct);
    }

    private async Task<OrderResult> ExecuteOrderAsync(Order order, OrderType orderType, decimal? price, decimal? stopPrice = null, CancellationToken ct = default)
    {
        try
        {
            var exchange = await exchangeRepo.GetByIdAsync(order.ExchangeId, ct);
            if (exchange is null)
                return new OrderResult(false, null, 0, 0, 0, "交易所不存在");

            var client = exchangeFactory.CreateClient(
                exchange.Type,
                encryptionService.Decrypt(exchange.ApiKeyEncrypted),
                encryptionService.Decrypt(exchange.SecretKeyEncrypted),
                exchange.PassphraseEncrypted is not null ? encryptionService.Decrypt(exchange.PassphraseEncrypted) : null);

            var symbol = order.SymbolId;
            var quantity = order.Side == OrderSide.Buy ? order.QuoteQuantity : order.Quantity;

            logger.LogInformation("执行{OrderSide}{OrderType}单: {Symbol} {Quantity} @ {Price}, 订单Id={OrderId}",
                order.Side, orderType, symbol, quantity, price, order.Id);

            return await client.PlaceOrderAsync(new OrderRequest(
                symbol, order.Side, orderType, quantity, price, stopPrice), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{OrderType}执行失败, 订单Id={OrderId}", orderType, order.Id);
            return new OrderResult(false, null, 0, 0, 0, ex.Message);
        }
    }
}
