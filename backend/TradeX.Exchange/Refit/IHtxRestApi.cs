using Refit;

namespace TradeX.Exchange.Refit;

public interface IHtxRestApi
{
    [Get("/market/history/kline")]
    Task<HtxResponse<List<HtxKline>>> GetKlinesAsync(
        [Query] string symbol,
        [Query] string period,
        [Query] int? size = null,
        CancellationToken ct = default);

    [Get("/market/depth")]
    Task<HtxResponse<HtxOrderBook>> GetOrderBookAsync(
        [Query] string symbol,
        [Query] string type,
        [Query] int? depth = null,
        CancellationToken ct = default);

    [Get("/market/tickers")]
    Task<HtxResponse<List<HtxTicker>>> GetTickersAsync(CancellationToken ct = default);

    [Get("/v1/common/symbols")]
    Task<HtxResponse<List<HtxSymbol>>> GetSymbolsAsync(CancellationToken ct = default);

    [Get("/v1/account/accounts")]
    Task<HtxResponse<List<HtxAccount>>> GetAccountsAsync(CancellationToken ct = default);

    [Get("/v1/account/accounts/{accountId}/balance")]
    Task<HtxSingleResponse<HtxBalanceData>> GetAccountBalanceAsync(long accountId, CancellationToken ct = default);

    [Post("/v1/order/orders/place")]
    Task<HtxOrderResponse> PlaceOrderAsync([Body] HtxPlaceOrderRequest request, CancellationToken ct = default);

    [Post("/v1/order/orders/{orderId}/submitcancel")]
    Task<HtxBasicResponse> CancelOrderAsync(string orderId, CancellationToken ct = default);

    [Get("/v1/order/orders/{orderId}")]
    Task<HtxSingleResponse<HtxOrderDetail>> GetOrderAsync(string orderId, CancellationToken ct = default);

    [Get("/v1/order/orders")]
    Task<HtxResponse<List<HtxOrderDetail>>> GetOrdersAsync(
        [Query] string? symbol = null,
        [Query] string? states = null,
        [Query] string? types = null,
        [Query] long? startTime = null,
        [Query] int? size = null,
        CancellationToken ct = default);

    [Get("/v1/order/openOrders")]
    Task<HtxResponse<List<HtxOrderDetail>>> GetOpenOrdersAsync(
        [Query("account-id")] long? accountId = null,
        [Query] string? symbol = null,
        [Query] int? size = null,
        CancellationToken ct = default);
}

public record HtxResponse<T>(
    string Status,
    T Data);

public record HtxSingleResponse<T>(
    string Status,
    T? Data = default);

public record HtxBasicResponse(
    string Status);

public record HtxKline(
    long Id,
    string Open,
    string High,
    string Low,
    string Close,
    string Vol,
    string Amount);

public record HtxOrderBook(
    string[][] Bids,
    string[][] Asks);

public record HtxTicker(
    string Symbol,
    string Close,
    string High,
    string Low,
    string Vol,
    string Amount,
    string PercentChange);

public record HtxSymbol(
    string Symbol,
    string State,
    int PricePrecision,
    int AmountPrecision,
    string MinOrderAmt);

public record HtxAccount(
    long Id,
    string Type,
    string State);

public record HtxBalanceData(
    long Id,
    string Type,
    string State,
    List<HtxBalanceEntry> List);

public record HtxBalanceEntry(
    string Currency,
    string Type,
    string Balance);

public record HtxPlaceOrderRequest(
    string AccountId,
    string Symbol,
    string Type,
    string Amount,
    string? Price = null);

public record HtxOrderResponse(
    string Status,
    string? Data = null);

public record HtxOrderDetail(
    long Id,
    string Symbol,
    string Type,
    string State,
    string Price,
    string Amount,
    string FilledAmount,
    string FilledCashAmount,
    string FieldFees,
    long CreatedAt);
