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
    Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default);
    Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default);
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
    decimal? StopPrice = null);

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
