using Refit;

namespace TradeX.Exchange.Refit;

public interface IBybitRestApi
{
    [Get("/v5/market/kline")]
    Task<BybitResponse<BybitKlineResult>> GetKlinesAsync(
        [Query] string category,
        [Query] string symbol,
        [Query] string interval,
        [Query] long? start = null,
        [Query] long? end = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/v5/market/orderbook")]
    Task<BybitResponse<BybitOrderBook>> GetOrderBookAsync(
        [Query] string category,
        [Query] string symbol,
        [Query] int limit = 1,
        CancellationToken ct = default);

    [Get("/v5/account/wallet-balance")]
    Task<BybitResponse<BybitWalletResult>> GetWalletBalanceAsync(
        [Query] string accountType,
        [Query] string? coin = null,
        CancellationToken ct = default);

    [Get("/v5/market/instruments-info")]
    Task<BybitResponse<BybitInstrumentsResult>> GetInstrumentsInfoAsync(
        [Query] string category,
        [Query] string? symbol = null,
        [Query] string? status = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/v5/market/tickers")]
    Task<BybitResponse<BybitTickersResult>> GetTickersAsync(
        [Query] string category,
        CancellationToken ct = default);

    [Post("/v5/order/create")]
    Task<BybitResponse<BybitOrderResult>> PlaceOrderAsync([Body] BybitPlaceOrderRequest request, CancellationToken ct = default);

    [Post("/v5/order/cancel")]
    Task<BybitResponse<BybitOrderResult>> CancelOrderAsync([Body] BybitCancelOrderRequest request, CancellationToken ct = default);

    [Get("/v5/order/realtime")]
    Task<BybitResponse<BybitOrdersResult>> GetOpenOrdersAsync(
        [Query] string? category = null,
        [Query] string? symbol = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/v5/order/history")]
    Task<BybitResponse<BybitOrdersResult>> GetOrderHistoryAsync(
        [Query] string? category = null,
        [Query] string? symbol = null,
        [Query] int? limit = null,
        CancellationToken ct = default);
}

public record BybitResponse<T>(
    int RetCode,
    string RetMsg,
    T Result);

public record BybitKlineResult(
    string Category,
    List<string[]> List);

public record BybitOrderBook(
    long Ts,
    string[][] B,
    string[][] A);

public record BybitWalletResult(
    List<BybitWalletAccount> List);

public record BybitWalletAccount(
    string AccountType,
    List<BybitCoinBalance> Coin);

public record BybitCoinBalance(
    string Coin,
    string WalletBalance);

public record BybitInstrumentsResult(
    string Category,
    List<BybitInstrumentInfo> List);

public record BybitInstrumentInfo(
    string Symbol,
    string Status,
    string BaseCoin,
    string QuoteCoin,
    BybitLotSizeFilter LotSizeFilter,
    BybitPriceFilter PriceFilter);

public record BybitLotSizeFilter(
    string MinOrderQty,
    string MinOrderAmt,
    string QtyStep);

public record BybitPriceFilter(
    string TickSize);

public record BybitTickersResult(
    string Category,
    List<BybitTicker> List);

public record BybitTicker(
    string Symbol,
    string LastPrice,
    string Price24hPcnt,
    string Volume24h,
    string HighPrice24h,
    string LowPrice24h);

public record BybitPlaceOrderRequest(
    string Category,
    string Symbol,
    string Side,
    string OrderType,
    string Qty,
    string? Price = null,
    string? TimeInForce = null);

public record BybitCancelOrderRequest(
    string Category,
    string Symbol,
    string? OrderId = null);

public record BybitOrderResult(
    string OrderId,
    string? OrderStatus = null);

public record BybitOrdersResult(
    string Category,
    List<BybitOrderDetails> List);

public record BybitOrderDetails(
    string OrderId,
    string Symbol,
    string Side,
    string OrderType,
    string OrderStatus,
    string Price,
    string Qty,
    string CumExecQty,
    string CumExecFee,
    string CreatedTime);
