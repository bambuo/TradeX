using TradeX.Core.Enums;

namespace TradeX.Core.Interfaces;

// ─────────────────────────────────────────────────────────────────────────────
// 拆分后的能力接口：消费方按需注入更窄的接口，便于 mock 和未来扩展。
// 现有代码仍可注入聚合接口 IExchangeClient，向后零破坏。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>行情数据：K 线、深度、ticker。无认证读端点。</summary>
public interface IMarketDataClient
{
    IAsyncEnumerable<Candle> SubscribeKlinesAsync(string Pair, string interval, CancellationToken ct = default);
    Task<Candle[]> GetKlinesAsync(string Pair, string interval, DateTime start, DateTime end, CancellationToken ct = default);
    Task<OrderBook> GetOrderBookAsync(string Pair, int limit, CancellationToken ct = default);
    Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default);
}

/// <summary>账户/持仓查询。读端点但需认证。</summary>
public interface IAccountClient
{
    Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default);
    Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default);
    Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default);
    Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default);
}

/// <summary>下单 / 撤单 / 订单查询。写端点。</summary>
public interface ITradingClient
{
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
}

/// <summary>管理类操作：连接测试、规则元数据。仅启动/配置场景使用。</summary>
public interface IExchangeAdminClient
{
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
    Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default);
}

/// <summary>
/// 聚合接口 — 兼容旧代码（注入 IExchangeClient 仍可拿到全部能力）。
/// 新代码建议注入更窄的子接口，让 Mock 测试更聚焦、依赖更明确。
/// </summary>
public interface IExchangeClient : IMarketDataClient, IAccountClient, ITradingClient, IExchangeAdminClient
{
    ExchangeType Type { get; }
}

// ─────────────────────────────────────────────────────────────────────────────
// 共享 DTO（保留原命名空间位置以避免下游引用变更）
// ─────────────────────────────────────────────────────────────────────────────

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
