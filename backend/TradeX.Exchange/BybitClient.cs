using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public class BybitClient : IExchangeClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly HMACSHA256 _hmac;

    public ExchangeType Type => ExchangeType.Bybit;

    public BybitClient(string apiKey, string secretKey, bool isTestnet)
    {
        _apiKey = apiKey;
        _hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        _http = new HttpClient
        {
            BaseAddress = new Uri(isTestnet ? "https://api-testnet.bybit.com" : "https://api.bybit.com")
        };
        _http.DefaultRequestHeaders.Add("X-BAPI-API-KEY", apiKey);
    }

    public async IAsyncEnumerable<Candle> SubscribeKlinesAsync(string symbol, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastTime = 0L;
        while (!ct.IsCancellationRequested)
        {
            var candles = await GetKlinesAsync(symbol, interval, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, ct);
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

    public async Task<Candle[]> GetKlinesAsync(string symbol, string interval, DateTime start, DateTime end, CancellationToken ct = default)
    {
        var category = "spot";
        var startMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();
        var query = $"category={category}&symbol={symbol}&interval={interval}&start={startMs}&end={endMs}&limit=200";
        var resp = await _http.GetAsync($"/v5/market/kline?{query}", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];
        if (doc.RootElement.GetProperty("retCode").GetInt32() != 0) return [];

        var list = doc.RootElement.GetProperty("result").GetProperty("list").EnumerateArray();
        return list.Select(k =>
        {
            var arr = k.EnumerateArray().ToList();
            return new Candle(
                DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(arr[0].GetString()!)).UtcDateTime,
                decimal.Parse(arr[1].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[2].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[3].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[4].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[5].GetString()!, CultureInfo.InvariantCulture));
        }).ToArray();
    }

    public async Task<OrderBook> GetOrderBookAsync(string symbol, int limit, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/v5/market/orderbook?category=spot&symbol={symbol}&limit={limit}", ct);
        if (!resp.IsSuccessStatusCode) return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("retCode").GetInt32() != 0)
            return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var result = doc.RootElement.GetProperty("result");
        var bids = ParseDepthEntries(result.GetProperty("b"));
        var asks = ParseDepthEntries(result.GetProperty("a"));
        return new OrderBook(bids, asks, DateTime.UtcNow);
    }

    public async Task<AccountBalance> GetBalanceAsync(CancellationToken ct = default)
    {
        var query = $"accountType=UNIFIED&coin=USDT";
        var doc = await SignedGetAsync("/v5/account/wallet-balance", query, ct);
        if (doc is null) return new AccountBalance(0, 0, 0);

        var list = doc.RootElement.GetProperty("result").GetProperty("list").EnumerateArray().FirstOrDefault();
        if (list.ValueKind == JsonValueKind.Undefined) return new AccountBalance(0, 0, 0);

        var coin = list.GetProperty("coin").EnumerateArray().FirstOrDefault(c => c.GetProperty("coin").GetString() == "USDT");
        if (coin.ValueKind == JsonValueKind.Undefined) return new AccountBalance(0, 0, 0);

        var walletBalance = decimal.Parse(coin.GetProperty("walletBalance").GetString()!, CultureInfo.InvariantCulture);
        return new AccountBalance(walletBalance, walletBalance, 0);
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default) => [];

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var side = request.Side == OrderSide.Buy ? "Buy" : "Sell";
        var orderType = request.Type == OrderType.Limit ? "Limit" : "Market";
        var bodyDict = new Dictionary<string, string>
        {
            ["category"] = "spot",
            ["symbol"] = request.Symbol,
            ["side"] = side,
            ["orderType"] = orderType,
            ["qty"] = request.Quantity.ToString(CultureInfo.InvariantCulture),
            ["timeInForce"] = "GTC"
        };
        if (request.Price.HasValue)
            bodyDict["price"] = request.Price.Value.ToString(CultureInfo.InvariantCulture)!;

        var bodyJson = JsonSerializer.Serialize(bodyDict);
        var doc = await SignedPostAsync("/v5/order/create", bodyJson, ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "请求失败");

        var retCode = doc.RootElement.GetProperty("retCode").GetInt32();
        if (retCode != 0)
        {
            var msg = doc.RootElement.GetProperty("retMsg").GetString();
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {msg}");
        }

        var result = doc.RootElement.GetProperty("result");
        var orderId = result.GetProperty("orderId").GetString();
        return new OrderResult(true, orderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var body = new { category = "spot", symbol = "BTCUSDT", orderId = exchangeOrderId };
        var doc = await SignedPostAsync("/v5/order/cancel", JsonSerializer.Serialize(body), ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "撤单请求失败");
        return doc.RootElement.GetProperty("retCode").GetInt32() == 0
            ? new OrderResult(true, exchangeOrderId, 0, 0, 0, null)
            : new OrderResult(false, null, 0, 0, 0, "撤单失败");
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var query = $"category=spot&symbol=BTCUSDT&orderId={exchangeOrderId}";
        var doc = await SignedGetAsync("/v5/order/realtime", query, ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "查询失败");

        if (doc.RootElement.GetProperty("retCode").GetInt32() != 0)
            return new OrderResult(false, null, 0, 0, 0, "订单不存在");

        var list = doc.RootElement.GetProperty("result").GetProperty("list").EnumerateArray().FirstOrDefault();
        if (list.ValueKind == JsonValueKind.Undefined) return new OrderResult(false, null, 0, 0, 0, "订单不存在");

        var filled = decimal.Parse(list.GetProperty("cumExecQty").GetString()!, CultureInfo.InvariantCulture);
        return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var startMs = new DateTimeOffset(since).ToUnixTimeMilliseconds();
        var query = $"category=spot&symbol=BTCUSDT&startTime={startMs}&limit=50";
        var doc = await SignedGetAsync("/v5/order/history", query, ct);
        if (doc is null) return [];

        var retCode = doc.RootElement.GetProperty("retCode").GetInt32();
        if (retCode != 0) return [];

        var list = doc.RootElement.GetProperty("result").GetProperty("list").EnumerateArray();
        return list.Select(o => new OrderResult(
            o.GetProperty("orderStatus").GetString() == "Filled",
            o.GetProperty("orderId").GetString(),
            decimal.Parse(o.GetProperty("cumExecQty").GetString()!, CultureInfo.InvariantCulture),
            0,
            decimal.Parse(o.GetProperty("cumExecFee").GetString()!, CultureInfo.InvariantCulture), null
        )).ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var doc = await SignedGetAsync("/v5/account/wallet-balance", "accountType=UNIFIED&coin=USDT", ct);
            if (doc is null) return new ConnectionTestResult(false, null, "连接失败");

            var retCode = doc.RootElement.GetProperty("retCode").GetInt32();
            return retCode == 0
                ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful")
                : new ConnectionTestResult(false, null, $"API 错误: {doc.RootElement.GetProperty("retMsg").GetString()}");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}");
        }
    }

    public async Task<SymbolRule[]> GetSymbolRulesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/v5/market/instruments-info?category=spot", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("retCode").GetInt32() != 0) return [];

        var list = doc.RootElement.GetProperty("result").GetProperty("list").EnumerateArray()
            .Where(s => s.GetProperty("status").GetString() == "Trading");
        return list.Select(s => new SymbolRule(
            s.GetProperty("symbol").GetString()!,
            s.GetProperty("priceFilter").GetProperty("tickSize").GetString()!.Length - 2,
            s.GetProperty("lotSizeFilter").GetProperty("qtyStep").GetString()!.Length - 2,
            decimal.Parse(s.GetProperty("lotSizeFilter").GetProperty("minOrderAmt").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(s.GetProperty("lotSizeFilter").GetProperty("minOrderQty").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(s.GetProperty("priceFilter").GetProperty("tickSize").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(s.GetProperty("lotSizeFilter").GetProperty("qtyStep").GetString()!, CultureInfo.InvariantCulture)
        )).ToArray();
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/v5/market/tickers?category=spot", ct);
        if (!resp.IsSuccessStatusCode) return [];
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("retCode").GetInt32() != 0) return [];

        return doc.RootElement.GetProperty("result").GetProperty("list").EnumerateArray().Select(t => new TickerPrice(
            t.GetProperty("symbol").GetString()!,
            TryParseDecimal(t.GetProperty("lastPrice").GetString()),
            TryParseDecimal(t.GetProperty("price24hPcnt").GetString()),
            TryParseDecimal(t.GetProperty("volume24h").GetString()),
            TryParseDecimal(t.GetProperty("highPrice24h").GetString()),
            TryParseDecimal(t.GetProperty("lowPrice24h").GetString())
        )).ToArray();
    }

    private async Task<JsonDocument?> SignedGetAsync(string path, string query, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var recvWindow = 5000;
        var signPayload = $"{timestamp}{_apiKey}{recvWindow}{query}";
        var sign = Sign(signPayload);

        var req = new HttpRequestMessage(HttpMethod.Get, $"{path}?{query}");
        req.Headers.Add("X-BAPI-TIMESTAMP", timestamp.ToString());
        req.Headers.Add("X-BAPI-SIGN", sign);
        req.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow.ToString());

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private async Task<JsonDocument?> SignedPostAsync(string path, string body, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var recvWindow = 5000;
        var signPayload = $"{timestamp}{_apiKey}{recvWindow}{body}";
        var sign = Sign(signPayload);

        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-BAPI-TIMESTAMP", timestamp.ToString());
        req.Headers.Add("X-BAPI-SIGN", sign);
        req.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow.ToString());

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private string Sign(string payload)
    {
        var hash = _hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    private static decimal TryParseDecimal(string? s)
    {
        if (s is null) return 0;
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
        return v;
    }

    private static decimal[,] ParseDepthEntries(JsonElement entries)
    {
        var list = entries.EnumerateArray().ToList();
        var result = new decimal[list.Count, 2];
        for (var i = 0; i < list.Count; i++)
        {
            result[i, 0] = decimal.Parse(list[i][0].GetString()!, CultureInfo.InvariantCulture);
            result[i, 1] = decimal.Parse(list[i][1].GetString()!, CultureInfo.InvariantCulture);
        }
        return result;
    }
}
