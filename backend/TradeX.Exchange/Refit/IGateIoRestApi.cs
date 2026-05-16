using Refit;

namespace TradeX.Exchange.Refit;

public interface IGateIoRestApi
{
    [Get("/api/v4/spot/candlesticks")]
    Task<List<List<string>>> GetCandlesticksAsync(
        [Query] string currency_pair,
        [Query] string interval,
        [Query] long? from = null,
        [Query] long? to = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/api/v4/spot/order_book")]
    Task<GateIoOrderBook> GetOrderBookAsync(
        [Query] string currency_pair,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/api/v4/spot/accounts")]
    Task<List<GateIoBalance>> GetAccountsAsync(CancellationToken ct = default);

    [Get("/api/v4/spot/currency_pairs")]
    Task<List<GateIoCurrencyPair>> GetCurrencyPairsAsync(CancellationToken ct = default);

    [Get("/api/v4/spot/tickers")]
    Task<List<GateIoTicker>> GetTickersAsync(CancellationToken ct = default);

    [Post("/api/v4/spot/orders")]
    Task<GateIoOrderResponse> PlaceOrderAsync([Body] GateIoPlaceOrderRequest request, CancellationToken ct = default);

    [Delete("/api/v4/spot/orders/{orderId}")]
    Task<GateIoOrderResponse> CancelOrderAsync(string orderId, CancellationToken ct = default);

    [Get("/api/v4/spot/orders/{orderId}")]
    Task<GateIoOrderResponse> GetOrderAsync(
        string orderId,
        [Query] string? currency_pair = null,
        CancellationToken ct = default);

    [Get("/api/v4/spot/orders")]
    Task<List<GateIoOrderResponse>> GetOrdersAsync(
        [Query] string? currency_pair = null,
        [Query] string? status = null,
        [Query] long? from = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/api/v4/spot/open_orders")]
    Task<List<GateIoOrderResponse>> GetOpenOrdersAsync(
        [Query] string? currency_pair = null,
        CancellationToken ct = default);
}

public record GateIoOrderBook(
    string[][] Bids,
    string[][] Asks);

public record GateIoBalance(
    string Currency,
    string Available,
    string Locked);

public record GateIoCurrencyPair(
    string Id,
    string TradeStatus,
    int Precision,
    int AmountPrecision,
    string MinQuoteAmount,
    string MinBaseAmount);

public record GateIoTicker(
    string CurrencyPair,
    string Last,
    string ChangePercentage,
    string BaseVolume,
    string High24h,
    string Low24h);

public record GateIoPlaceOrderRequest(
    string CurrencyPair,
    string Side,
    string Amount,
    string Type,
    string? Price = null,
    string? TimeInForce = null,
    /// <summary>客户端订单 ID。必须以 "t-" 开头，总长度 ≤28 字符（即 t- + 最多 26 字符）。</summary>
    string? Text = null);

public record GateIoOrderResponse(
    string Id,
    string CurrencyPair,
    string Side,
    string Type,
    string Status,
    string Price,
    string Amount,
    string FilledAmount,
    string FilledTotal,
    string Fee,
    string CreateTime,
    string? Label = null);
