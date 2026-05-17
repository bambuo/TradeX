using TradeX.Core.Enums;

namespace TradeX.Core.Interfaces;

public interface IExchangeClient
{
    ExchangeType Type { get; }

    IAsyncEnumerable<Candle> SubscribeKlinesAsync(string Pair, string interval, CancellationToken ct = default);
    Task<Candle[]> GetKlinesAsync(string Pair, string interval, DateTime start, DateTime end, CancellationToken ct = default);
    Task<OrderBook> GetOrderBookAsync(string Pair, int limit, CancellationToken ct = default);

    Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default);
    Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default);

    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default);
    /// <summary>查询订单状态。各交易所均要求 pair 标识，不再硬编码 BTCUSDT。</summary>
    Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default);

    /// <summary>
    /// 凭 ClientOrderId 反查交易所订单状态。供 <c>OrderReconciler</c> 在订单缺少 ExchangeOrderId
    /// (pre-persist 后崩溃) 时确认交易所是否真的没收到该订单。
    /// 各交易所参数名不同 (Binance: origClientOrderId, OKX: clOrdId, Bybit: orderLinkId,
    /// Gate: text, HTX: client-order-id)。未实现该接口的客户端应返回 Success=false +
    /// Error="not_supported"，对账器据此降级到超时判定。
    /// </summary>
    Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default);
    Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);

    Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default);

    Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default);

    Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default);
    Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default);
}

public record Candle(DateTime Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

public record OrderBook(
    decimal[,] Bids,
    decimal[,] Asks,
    DateTime Timestamp);

public record OrderRequest(
    string Pair,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price = null,
    decimal? StopPrice = null,
    /// <summary>幂等键。各交易所支持的字段名不同（Binance: newClientOrderId, OKX: clOrdId,
    /// Bybit: orderLinkId, Gate: text, HTX: client-order-id），客户端实现可选择透传至 API。
    /// 即使未透传，调用方仍可凭此键在 DB 层做去重和对账。</summary>
    string? ClientOrderId = null);

public record OrderResult(
    bool Success,
    string? ExchangeOrderId,
    decimal FilledQuantity,
    decimal AvgPrice,
    decimal Fee,
    string? Error);

public record ExchangePosition(
    string Pair,
    decimal Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal UnrealizedPnl);

public record PairRule(
    string Pair,
    int PricePrecision,
    int QuantityPrecision,
    decimal MinNotional,
    decimal MinQuantity,
    decimal TickSize,
    decimal StepSize);

public record ExchangeOrderDto(
    string Pair,
    string Side,
    string Type,
    string Status,
    decimal Price,
    decimal Quantity,
    decimal FilledQuantity,
    string ExchangeOrderId,
    DateTime PlacedAt);

public record ConnectionTestResult(
    bool Success,
    Dictionary<string, bool>? Permissions,
    string? Message);

public record TickerPrice(
    string Pair,
    decimal Price,
    decimal PriceChangePercent,
    decimal Volume,
    decimal HighPrice,
    decimal LowPrice);
