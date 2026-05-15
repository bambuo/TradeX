using Refit;

namespace TradeX.Exchange.Refit;

public interface IOkxRestApi
{
    [Get("/api/v5/market/history-candles")]
    Task<OkxResponse<List<OkxKline>>> GetKlinesAsync(
        [Query] string instId,
        [Query] string bar,
        [Query] long? after = null,
        [Query] long? before = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/api/v5/market/books")]
    Task<OkxResponse<List<OkxOrderBook>>> GetOrderBookAsync(
        [Query] string instId,
        [Query] int? sz = null,
        CancellationToken ct = default);

    [Get("/api/v5/market/tickers")]
    Task<OkxResponse<List<OkxTicker>>> GetTickersAsync(
        [Query] string instType,
        CancellationToken ct = default);

    [Get("/api/v5/public/instruments")]
    Task<OkxResponse<List<OkxInstrument>>> GetInstrumentsAsync(
        [Query] string instType,
        CancellationToken ct = default);

    [Get("/api/v5/account/balance")]
    Task<OkxResponse<List<OkxAccountBalance>>> GetAccountBalanceAsync(CancellationToken ct = default);

    [Post("/api/v5/trade/order")]
    Task<OkxResponse<List<OkxOrderResult>>> PlaceOrderAsync([Body] OkxPlaceOrderRequest request, CancellationToken ct = default);

    [Post("/api/v5/trade/cancel-order")]
    Task<OkxResponse<List<OkxOrderResult>>> CancelOrderAsync([Body] OkxCancelOrderRequest request, CancellationToken ct = default);

    [Get("/api/v5/trade/order")]
    Task<OkxResponse<List<OkxOrderDetails>>> GetOrderAsync(
        [Query] string instId,
        [Query] string ordId,
        CancellationToken ct = default);

    [Get("/api/v5/trade/orders-history")]
    Task<OkxResponse<List<OkxOrderDetails>>> GetOrderHistoryAsync(
        [Query] string instId,
        [Query] string? after = null,
        [Query] int? limit = null,
        CancellationToken ct = default);

    [Get("/api/v5/trade/orders-pending")]
    Task<OkxResponse<List<OkxOrderDetails>>> GetPendingOrdersAsync(
        [Query] string? instType = null,
        CancellationToken ct = default);
}

public record OkxResponse<T>(
    string Code,
    string Msg,
    T Data);

public record OkxKline(
    string Ts,
    string O,
    string H,
    string L,
    string C,
    string Vol,
    string VolCcy,
    string VolCcyQuote,
    string Confirm);

public record OkxOrderBook(
    string Ts,
    decimal[][] Bids,
    decimal[][] Asks);

public record OkxTicker(
    string InstId,
    string Last,
    string LastSz,
    string AskPx,
    string BidPx,
    string VolCcy24h,
    string Vol24h,
    string High24h,
    string Low24h,
    string ChangePercent);

public record OkxInstrument(
    string InstId,
    string State,
    string TickSz,
    string LotSz,
    string MinSz);

public record OkxAccountBalance(
    string UTime,
    OkxBalanceDetail[] Details);

public record OkxBalanceDetail(
    string Ccy,
    string CashBal);

public record OkxPlaceOrderRequest(
    string InstId,
    string TdMode,
    string Side,
    string OrdType,
    string Sz,
    string? Px = null);

public record OkxCancelOrderRequest(
    string InstId,
    string OrdId);

public record OkxOrderResult(
    string OrdId,
    string? SCode = null,
    string? SMsg = null);

public record OkxOrderDetails(
    string InstId,
    string OrdId,
    string Side,
    string OrdType,
    string State,
    string Px,
    string Sz,
    string AccFillSz,
    string Fee,
    string CTime);
