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

public class GateIoClient(string apiKey, string secretKey) : IExchangeClient
{
    private readonly IGateIoRestApi _api = CreateRefitClient(apiKey, secretKey);

    public ExchangeType Type => ExchangeType.Gate;

    private static IGateIoRestApi CreateRefitClient(string key, string secret)
    {
        var auth = new GateIoAuthHandler(key, secret)
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10
            }
        };
        var handler = new ResilienceHandler(ExchangeType.Gate) { InnerHandler = auth };

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.gateio.ws") };
        return RestService.For<IGateIoRestApi>(http, new RefitSettings
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
        var from = new DateTimeOffset(start).ToUnixTimeSeconds();
        var to = new DateTimeOffset(end).ToUnixTimeSeconds();

        try
        {
            var raw = await _api.GetCandlesticksAsync(Pair, interval, from, to, 500, ct);
            return raw.Select(k =>
            {
                return new Candle(
                    DateTimeOffset.FromUnixTimeSeconds(long.Parse(k[0])).UtcDateTime,
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
            var resp = await _api.GetOrderBookAsync(Pair, limit, ct);
            var bids = ParseDepthEntries(resp.Bids);
            var asks = ParseDepthEntries(resp.Asks);
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
            var balances = await _api.GetAccountsAsync(ct);
            Dictionary<string, decimal> result = [];
            foreach (var b in balances)
            {
                var available = decimal.Parse(b.Available, CultureInfo.InvariantCulture);
                var locked = decimal.Parse(b.Locked, CultureInfo.InvariantCulture);
                var total = available + locked;
                if (total > 0)
                    result[b.Currency] = total;
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
        var side = request.Side == OrderSide.Buy ? "buy" : "sell";
        // Gate text 限制：必须以 "t-" 开头、总长度 ≤28；GUID N 是 32 hex 字符，截取前 26 个 + "t-" 前缀正好 28
        var text = string.IsNullOrEmpty(request.ClientOrderId)
            ? null
            : "t-" + (request.ClientOrderId.Length > 26 ? request.ClientOrderId[..26] : request.ClientOrderId);
        var body = new GateIoPlaceOrderRequest(
            request.Pair, side,
            request.Quantity.ToString(CultureInfo.InvariantCulture),
            request.Type == OrderType.Limit ? "limit" : "market",
            request.Price?.ToString(CultureInfo.InvariantCulture),
            "gtc",
            Text: text);

        try
        {
            var resp = await _api.PlaceOrderAsync(body, ct);
            if (!string.IsNullOrEmpty(resp.Label))
                return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {resp.Label}");

            return new OrderResult(true, resp.Id, 0, 0, 0, null);
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {ex.Message}");
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.CancelOrderAsync(exchangeOrderId, ct);
            return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
        }
        catch (ApiException)
        {
            return new OrderResult(false, null, 0, 0, 0, "撤单失败");
        }
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        try
        {
            // Gate API: order_id 路径占位符接受 "t-{text}"；需要 currency_pair query 参数
            var text = "t-" + (clientOrderId.Length > 26 ? clientOrderId[..26] : clientOrderId);
            var resp = await _api.GetOrderAsync(text, currency_pair: pair, ct: ct);
            var filled = decimal.TryParse(resp.FilledAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 0;
            var fee = decimal.TryParse(resp.Fee, NumberStyles.Any, CultureInfo.InvariantCulture, out var fee0) ? fee0 : 0;
            return new OrderResult(true, resp.Id, filled, 0, fee, null);
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
            var resp = await _api.GetOrderAsync(exchangeOrderId, ct: ct);
            var filled = decimal.Parse(resp.FilledTotal, CultureInfo.InvariantCulture);
            return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
        }
        catch (ApiException)
        {
            return new OrderResult(false, null, 0, 0, 0, "查询失败");
        }
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var from = new DateTimeOffset(since).ToUnixTimeSeconds();

        try
        {
            var orders = await _api.GetOrdersAsync("BTCUSDT", from: from, limit: 50, ct: ct);
            return orders.Select(o => new OrderResult(
                o.Status == "closed",
                o.Id,
                decimal.Parse(o.FilledTotal, CultureInfo.InvariantCulture),
                0,
                decimal.Parse(o.Fee, CultureInfo.InvariantCulture),
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
            var orders = await _api.GetOpenOrdersAsync(ct: ct);
            return orders.Select(ParseGateOrder).ToArray();
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
            var orders = await _api.GetOrdersAsync(status: "finished", limit: 50, ct: ct);
            return orders.Select(ParseGateOrder).ToArray();
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
            var balances = await _api.GetAccountsAsync(ct);
            return new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful");
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
            var pairs = await _api.GetCurrencyPairsAsync(ct);
            return pairs
                .Where(s => s.TradeStatus == "tradable")
                .Select(s => new PairRule(
                    s.Id,
                    s.Precision,
                    s.AmountPrecision,
                    decimal.Parse(s.MinQuoteAmount, CultureInfo.InvariantCulture),
                    decimal.Parse(s.MinBaseAmount, CultureInfo.InvariantCulture),
                    s.Precision,
                    1m / (decimal)Math.Pow(10, s.AmountPrecision)))
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
            var tickers = await _api.GetTickersAsync(ct);
            return tickers.Select(t => new TickerPrice(
                t.CurrencyPair,
                TryParseDecimal(t.Last),
                TryParseDecimal(t.ChangePercentage),
                TryParseDecimal(t.BaseVolume),
                TryParseDecimal(t.High24h),
                TryParseDecimal(t.Low24h))).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private static ExchangeOrderDto ParseGateOrder(GateIoOrderResponse o)
    {
        return new ExchangeOrderDto(
            o.CurrencyPair,
            o.Side == "buy" ? "Buy" : "Sell",
            o.Type == "limit" ? "Limit" : o.Type == "market" ? "Market" : o.Type,
            o.Status switch
            {
                "open" => "New",
                "filled" => "Filled",
                "cancelled" => "Cancelled",
                _ => o.Status
            },
            decimal.Parse(o.Price, CultureInfo.InvariantCulture),
            decimal.Parse(o.Amount, CultureInfo.InvariantCulture),
            decimal.Parse(o.FilledAmount, CultureInfo.InvariantCulture),
            o.Id,
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(o.CreateTime)).UtcDateTime
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
