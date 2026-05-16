using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Refit;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Exchange.Handlers;
using TradeX.Exchange.Refit;
using TradeX.Exchange.Resilience;

namespace TradeX.Exchange;

public class BybitClient(string apiKey, string secretKey, bool isTestnet) : IExchangeClient
{
    private readonly IBybitRestApi _api = CreateRefitClient(apiKey, secretKey, isTestnet);

    public ExchangeType Type => ExchangeType.Bybit;

    private static IBybitRestApi CreateRefitClient(string key, string secret, bool testnet)
    {
        var baseUri = new Uri(testnet ? "https://api-testnet.bybit.com" : "https://api.bybit.com");

        var auth = new BybitAuthHandler(key, secret)
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10
            }
        };
        var handler = new ResilienceHandler(ExchangeType.Bybit) { InnerHandler = auth };

        var http = new HttpClient(handler) { BaseAddress = baseUri };
        return RestService.For<IBybitRestApi>(http, new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            })
        });
    }

    public async IAsyncEnumerable<Candle> SubscribeKlinesAsync(string Pair, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastTime = 0L;
        while (!ct.IsCancellationRequested)
        {
            var candles = await GetKlinesAsync(Pair, interval, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, ct);
            foreach (var c in candles)
            {
                var ms = new DateTimeOffset(c.Timestamp).ToUnixTimeMilliseconds();
                if (ms > lastTime)
                {
                    lastTime = ms;
                    yield return c;
                }
            }
            await Task.Delay(1000, ct);
        }
    }

    public async Task<Candle[]> GetKlinesAsync(string Pair, string interval, DateTime start, DateTime end, CancellationToken ct = default)
    {
        var startMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();

        try
        {
            var resp = await _api.GetKlinesAsync("spot", Pair, interval, startMs, endMs, 200, ct);
            if (resp.RetCode != 0) return [];

            return resp.Result.List.Select(k =>
            {
                return new Candle(
                    DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(k[0])).UtcDateTime,
                    decimal.Parse(k[1], CultureInfo.InvariantCulture),
                    decimal.Parse(k[2], CultureInfo.InvariantCulture),
                    decimal.Parse(k[3], CultureInfo.InvariantCulture),
                    decimal.Parse(k[4], CultureInfo.InvariantCulture),
                    decimal.Parse(k[5], CultureInfo.InvariantCulture));
            }).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<OrderBook> GetOrderBookAsync(string Pair, int limit, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetOrderBookAsync("spot", Pair, limit, ct);
            if (resp.RetCode != 0)
                return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

            var bids = ParseDepthEntries(resp.Result.B);
            var asks = ParseDepthEntries(resp.Result.A);
            return new OrderBook(bids, asks, DateTime.UtcNow);
        }
        catch (ApiException)
        {
            return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);
        }
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetWalletBalanceAsync("UNIFIED", ct: ct);
            if (resp.RetCode != 0 || resp.Result.List.Count == 0) return [];

            Dictionary<string, decimal> result = [];
            foreach (var coin in resp.Result.List[0].Coin)
            {
                var walletBalance = decimal.Parse(coin.WalletBalance, CultureInfo.InvariantCulture);
                if (walletBalance > 0)
                    result[coin.Coin] = walletBalance;
            }
            return result;
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default) => [];

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var side = request.Side == OrderSide.Buy ? "Buy" : "Sell";
        var orderType = request.Type == OrderType.Limit ? "Limit" : "Market";
        var body = new BybitPlaceOrderRequest(
            "spot", request.Pair, side, orderType,
            request.Quantity.ToString(CultureInfo.InvariantCulture),
            request.Price?.ToString(CultureInfo.InvariantCulture),
            "GTC",
            // Bybit orderLinkId 限制：≤36 字符，字符集 [a-zA-Z0-9-_]；GUID N 格式 (32 hex) 完全符合
            OrderLinkId: request.ClientOrderId);

        try
        {
            var resp = await _api.PlaceOrderAsync(body, ct);
            if (resp.RetCode != 0)
                return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {resp.RetMsg}");

            return new OrderResult(true, resp.Result.OrderId, 0, 0, 0, null);
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {ex.Message}");
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var body = new BybitCancelOrderRequest("spot", "BTCUSDT", exchangeOrderId);
        try
        {
            var resp = await _api.CancelOrderAsync(body, ct);
            return resp.RetCode == 0
                ? new OrderResult(true, exchangeOrderId, 0, 0, 0, null)
                : new OrderResult(false, null, 0, 0, 0, "撤单失败");
        }
        catch (ApiException)
        {
            return new OrderResult(false, null, 0, 0, 0, "撤单请求失败");
        }
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        try
        {
            // 优先查 realtime（开放 + 已成交未归档），未命中则查 history
            var open = await _api.GetOpenOrdersAsync("spot", pair, orderLinkId: clientOrderId, ct: ct);
            var details = open.RetCode == 0 ? open.Result.List.FirstOrDefault() : null;
            if (details is null)
            {
                var hist = await _api.GetOrderHistoryAsync("spot", pair, orderLinkId: clientOrderId, ct: ct);
                details = hist.RetCode == 0 ? hist.Result.List.FirstOrDefault() : null;
            }
            if (details is null)
                return new OrderResult(false, null, 0, 0, 0, "交易所无此 OrderLinkId");

            var filled = decimal.Parse(details.CumExecQty, CultureInfo.InvariantCulture);
            var fee = decimal.Parse(details.CumExecFee, CultureInfo.InvariantCulture);
            return new OrderResult(true, details.OrderId, filled, 0, fee, null);
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"按 ClientOrderId 查询失败: {ex.StatusCode} {ex.Message}");
        }
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetOpenOrdersAsync("spot", "BTCUSDT", ct: ct);
            if (resp.RetCode != 0)
                return new OrderResult(false, null, 0, 0, 0, "查询失败");

            var order = resp.Result.List.FirstOrDefault(o => o.OrderId == exchangeOrderId);
            if (order is null)
                return new OrderResult(false, null, 0, 0, 0, "订单不存在");

            var filled = decimal.Parse(order.CumExecQty, CultureInfo.InvariantCulture);
            return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
        }
        catch (ApiException)
        {
            return new OrderResult(false, null, 0, 0, 0, "查询失败");
        }
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var startMs = new DateTimeOffset(since).ToUnixTimeMilliseconds();

        try
        {
            var resp = await _api.GetOrderHistoryAsync("spot", "BTCUSDT", limit: 50, ct: ct);
            if (resp.RetCode != 0) return [];

            return resp.Result.List
                .Where(o => long.TryParse(o.CreatedTime, out var t) && t >= startMs)
                .Select(o => new OrderResult(
                    o.OrderStatus == "Filled",
                    o.OrderId,
                    decimal.Parse(o.CumExecQty, CultureInfo.InvariantCulture),
                    0,
                    decimal.Parse(o.CumExecFee, CultureInfo.InvariantCulture),
                    null)).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetOpenOrdersAsync("spot", ct: ct);
            if (resp.RetCode != 0) return [];

            return resp.Result.List.Select(ParseBybitOrder).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetOrderHistoryAsync("spot", limit: 50, ct: ct);
            if (resp.RetCode != 0) return [];

            return resp.Result.List.Select(ParseBybitOrder).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetWalletBalanceAsync("UNIFIED", "USDT", ct);
            return resp.RetCode == 0
                ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful")
                : new ConnectionTestResult(false, null, $"API 错误: {resp.RetMsg}");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}");
        }
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetInstrumentsInfoAsync("spot", ct: ct);
            if (resp.RetCode != 0) return [];

            return resp.Result.List
                .Where(s => s.Status == "Trading")
                .Select(s => new PairRule(
                    s.Symbol,
                    s.PriceFilter.TickSize.Length - 2,
                    s.LotSizeFilter.QtyStep.Length - 2,
                    decimal.Parse(s.LotSizeFilter.MinOrderAmt, CultureInfo.InvariantCulture),
                    decimal.Parse(s.LotSizeFilter.MinOrderQty, CultureInfo.InvariantCulture),
                    decimal.Parse(s.PriceFilter.TickSize, CultureInfo.InvariantCulture),
                    decimal.Parse(s.LotSizeFilter.QtyStep, CultureInfo.InvariantCulture)))
                .ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetTickersAsync("spot", ct);
            if (resp.RetCode != 0) return [];

            return resp.Result.List.Select(t => new TickerPrice(
                t.Symbol,
                TryParseDecimal(t.LastPrice),
                TryParseDecimal(t.Price24hPcnt) * 100,
                TryParseDecimal(t.Volume24h),
                TryParseDecimal(t.HighPrice24h),
                TryParseDecimal(t.LowPrice24h))).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private static ExchangeOrderDto ParseBybitOrder(BybitOrderDetails o)
    {
        return new ExchangeOrderDto(
            o.Symbol,
            o.Side == "Buy" ? "Buy" : "Sell",
            o.OrderType switch { "Limit" => "Limit", "Market" => "Market", _ => o.OrderType },
            o.OrderStatus switch
            {
                "Created" => "New",
                "New" => "New",
                "PartiallyFilled" => "PartiallyFilled",
                "Filled" => "Filled",
                "Cancelled" => "Cancelled",
                _ => o.OrderStatus
            },
            TryParseDecimal(o.Price),
            TryParseDecimal(o.Qty),
            TryParseDecimal(o.CumExecQty),
            o.OrderId,
            DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(o.CreatedTime)).UtcDateTime
        );
    }

    private static decimal TryParseDecimal(string? s)
    {
        if (s is null) return 0;
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
        return v;
    }

    private static decimal[,] ParseDepthEntries(string[][] entries)
    {
        var result = new decimal[entries.Length, 2];
        for (var i = 0; i < entries.Length; i++)
        {
            result[i, 0] = decimal.Parse(entries[i][0], CultureInfo.InvariantCulture);
            result[i, 1] = decimal.Parse(entries[i][1], CultureInfo.InvariantCulture);
        }
        return result;
    }
}
