using Refit;

namespace TradeX.Exchange.Refit;

public interface IBinanceRestApi
{
    [Get("/api/v3/ping")]
    Task<long> PingAsync(CancellationToken ct = default);

    [Get("/api/v3/exchangeInfo")]
    Task<BinanceExchangeInfo> GetExchangeInfoAsync(CancellationToken ct = default);

    [Get("/api/v3/klines")]
    Task<List<BinanceKline>> GetKlinesAsync(
        [Query] string symbol,
        [Query] string interval,
        [Query] long? startTime = null,
        [Query] long? endTime = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/api/v3/depth")]
    Task<BinanceOrderBook> GetOrderBookAsync(
        [Query] string symbol,
        [Query] int limit = 100,
        CancellationToken ct = default);

    [Get("/api/v3/ticker/24hr")]
    Task<List<BinanceTicker>> GetAllTicker24HrAsync(CancellationToken ct = default);

    [Get("/api/v3/ticker/price")]
    Task<List<BinanceTickerPrice>> GetAllTickerPricesAsync(CancellationToken ct = default);

    [Get("/api/v3/account")]
    Task<BinanceAccountInfo> GetAccountInfoAsync(CancellationToken ct = default);

    [Post("/api/v3/order")]
    Task<BinanceOrderResponse> PlaceOrderAsync(
        [Query] string symbol,
        [Query] string side,
        [Query] string type,
        [Query] string? timeInForce = null,
        [Query] string? quantity = null,
        [Query] string? price = null,
        [Query] string? stopPrice = null,
        [Query] string? newClientOrderId = null,
        CancellationToken ct = default);

    [Delete("/api/v3/order")]
    Task<BinanceOrderResponse> CancelOrderAsync(
        [Query] string symbol,
        [Query] string? origClientOrderId = null,
        [Query] string? orderId = null,
        [Query] string? newClientOrderId = null,
        CancellationToken ct = default);

    [Get("/api/v3/order")]
    Task<BinanceOrderResponse> GetOrderAsync(
        [Query] string symbol,
        [Query] string? origClientOrderId = null,
        [Query] string? orderId = null,
        CancellationToken ct = default);

    [Get("/api/v3/openOrders")]
    Task<List<BinanceOrderResponse>> GetOpenOrdersAsync(
        [Query] string? symbol = null,
        CancellationToken ct = default);

    [Get("/api/v3/allOrders")]
    Task<List<BinanceOrderResponse>> GetAllOrdersAsync(
        [Query] string symbol,
        [Query] long? startTime = null,
        [Query] long? endTime = null,
        [Query] int? limit = null,
        CancellationToken ct = default);
}

public record BinanceKline(
    long OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    long CloseTime,
    decimal QuoteVolume,
    int Count,
    decimal TakerBuyVolume,
    decimal TakerBuyQuoteVolume,
    decimal Ignore);

public record BinanceOrderBook(
    decimal[][] Bids,
    decimal[][] Asks);

public record BinanceTicker(
    string Symbol,
    decimal PriceChange,
    decimal PriceChangePercent,
    decimal LastPrice,
    decimal BidPrice,
    decimal AskPrice,
    decimal Volume,
    decimal QuoteVolume,
    decimal HighPrice,
    decimal LowPrice);

public record BinanceTickerPrice(
    string Symbol,
    decimal Price);

public record BinanceExchangeInfo(
    string Timezone,
    long ServerTime,
    BinanceSymbolInfo[] Symbols);

public record BinanceSymbolInfo(
    string Symbol,
    string Status,
    string BaseAsset,
    string QuoteAsset,
    bool IsSpotTradingAllowed,
    BinanceSymbolFilter[] Filters);

public record BinanceSymbolFilter(
    string FilterType,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    decimal? TickSize = null,
    decimal? MinQty = null,
    decimal? MaxQty = null,
    decimal? StepSize = null,
    decimal? MinNotional = null);

public record BinanceAccountInfo(
    int MakerCommission,
    int TakerCommission,
    bool CanTrade,
    bool CanWithdraw,
    BinanceBalance[] Balances);

public record BinanceBalance(
    string Asset,
    decimal Free,
    decimal Locked);

public record BinanceOrderResponse(
    string Symbol,
    long OrderId,
    string ClientOrderId,
    long? OrderListId,
    string Status,
    string Side,
    string Type,
    string TimeInForce,
    decimal Price,
    decimal OrigQty,
    decimal ExecutedQty,
    decimal CumulativeQuoteQty,
    decimal? StopPrice = null,
    long? Time = null);
