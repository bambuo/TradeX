using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public class OkxClient : IExchangeClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _passphrase;
    private readonly HMACSHA256 _hmac;

    public ExchangeType Type => ExchangeType.OKX;

    public OkxClient(string apiKey, string secretKey, string? passphrase = null)
    {
        _apiKey = apiKey;
        _passphrase = passphrase ?? "";
        _hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        _http = new HttpClient { BaseAddress = new Uri("https://www.okx.com") };
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
        var after = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var before = new DateTimeOffset(end).ToUnixTimeMilliseconds();
        var resp = await _http.GetAsync($"/api/v5/market/history-candles?instId={symbol}&bar={interval}&after={after}&before={before}&limit=300", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("code").GetString() != "0") return [];

        var data = doc.RootElement.GetProperty("data").EnumerateArray();
        return data.Select(k =>
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
        var resp = await _http.GetAsync($"/api/v5/market/books?instId={symbol}&sz={limit}", ct);
        if (!resp.IsSuccessStatusCode) return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("code").GetString() != "0")
            return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var book = doc.RootElement.GetProperty("data")[0];
        var bids = ParseDepthEntries(book.GetProperty("bids"));
        var asks = ParseDepthEntries(book.GetProperty("asks"));
        return new OrderBook(bids, asks, DateTime.UtcNow);
    }

    public async Task<AccountBalance> GetBalanceAsync(CancellationToken ct = default)
    {
        var doc = await SignedGetAsync("/api/v5/account/balance", null, ct);
        if (doc is null) return new AccountBalance(0, 0, 0);

        var data = doc.RootElement.GetProperty("data")[0];
        var details = data.GetProperty("details").EnumerateArray();
        var usdt = details.FirstOrDefault(d => d.GetProperty("ccy").GetString() == "USDT");
        if (usdt.ValueKind == JsonValueKind.Undefined) return new AccountBalance(0, 0, 0);

        var cashBal = decimal.Parse(usdt.GetProperty("cashBal").GetString()!, CultureInfo.InvariantCulture);
        return new AccountBalance(cashBal, cashBal, 0);
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default) => [];

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var side = request.Side == OrderSide.Buy ? "buy" : "sell";
        var ordType = request.Type == OrderType.Limit ? "limit" : "market";
        var body = new
        {
            instId = request.Symbol,
            tdMode = "cash",
            side,
            ordType,
            sz = request.Quantity.ToString(CultureInfo.InvariantCulture),
            px = request.Price?.ToString(CultureInfo.InvariantCulture)
        };

        var doc = await SignedPostAsync("/api/v5/trade/order", JsonSerializer.Serialize(body), ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "请求失败");

        var code = doc.RootElement.GetProperty("code").GetString();
        if (code != "0")
        {
            var msg = doc.RootElement.GetProperty("msg").GetString();
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {msg}");
        }

        var data = doc.RootElement.GetProperty("data")[0];
        var orderId = data.GetProperty("ordId").GetString();
        return new OrderResult(true, orderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var body = new { instId = "BTCUSDT", ordId = exchangeOrderId };
        var doc = await SignedPostAsync("/api/v5/trade/cancel-order", JsonSerializer.Serialize(body), ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "撤单请求失败");
        return doc.RootElement.GetProperty("code").GetString() == "0"
            ? new OrderResult(true, exchangeOrderId, 0, 0, 0, null)
            : new OrderResult(false, null, 0, 0, 0, "撤单失败");
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var query = $"instId=BTCUSDT&ordId={exchangeOrderId}";
        var doc = await SignedGetAsync("/api/v5/trade/order", query, ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "查询失败");

        if (doc.RootElement.GetProperty("code").GetString() != "0")
            return new OrderResult(false, null, 0, 0, 0, "订单不存在");

        var data = doc.RootElement.GetProperty("data")[0];
        var filled = decimal.Parse(data.GetProperty("accFillSz").GetString()!, CultureInfo.InvariantCulture);
        return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var after = new DateTimeOffset(since).ToUnixTimeMilliseconds();
        var query = $"instId=BTCUSDT&after={after}&limit=50";
        var doc = await SignedGetAsync("/api/v5/trade/orders-history", query, ct);
        if (doc is null) return [];

        if (doc.RootElement.GetProperty("code").GetString() != "0") return [];

        return doc.RootElement.GetProperty("data").EnumerateArray().Select(o => new OrderResult(
            o.GetProperty("state").GetString() == "filled",
            o.GetProperty("ordId").GetString(),
            decimal.Parse(o.GetProperty("accFillSz").GetString()!, CultureInfo.InvariantCulture),
            0,
            decimal.Parse(o.GetProperty("fee").GetString()!, CultureInfo.InvariantCulture), null
        )).ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var doc = await SignedGetAsync("/api/v5/account/balance", null, ct);
            if (doc is null) return new ConnectionTestResult(false, null, "连接失败");

            var code = doc.RootElement.GetProperty("code").GetString();
            return code == "0"
                ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful")
                : new ConnectionTestResult(false, null, $"API 错误: {doc.RootElement.GetProperty("msg").GetString()}");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}");
        }
    }

    public async Task<SymbolRule[]> GetSymbolRulesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/v5/public/instruments?instType=SPOT", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("code").GetString() != "0") return [];

        var data = doc.RootElement.GetProperty("data").EnumerateArray()
            .Where(s => s.GetProperty("state").GetString() == "live");
        return data.Select(s => new SymbolRule(
            s.GetProperty("instId").GetString()!,
            s.GetProperty("tickSz").GetString()!.Length - 1,
            s.GetProperty("lotSz").GetString()!.Length - 1,
            decimal.Parse(s.GetProperty("minSz").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(s.GetProperty("minSz").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(s.GetProperty("tickSz").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(s.GetProperty("lotSz").GetString()!, CultureInfo.InvariantCulture)
        )).ToArray();
    }

    private async Task<JsonDocument?> SignedGetAsync(string path, string? query, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var requestPath = query is not null ? $"{path}?{query}" : path;
        var signPayload = $"{timestamp}GET{requestPath}";
        var sign = Sign(signPayload);

        var url = query is not null ? $"{path}?{query}" : path;
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeaders(req, timestamp.ToString(), sign);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private async Task<JsonDocument?> SignedPostAsync(string path, string body, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var signPayload = $"{timestamp}POST{path}{body}";
        var sign = Sign(signPayload);

        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        AddAuthHeaders(req, timestamp.ToString(), sign);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private void AddAuthHeaders(HttpRequestMessage req, string timestamp, string sign)
    {
        req.Headers.Add("OK-ACCESS-KEY", _apiKey);
        req.Headers.Add("OK-ACCESS-SIGN", sign);
        req.Headers.Add("OK-ACCESS-TIMESTAMP", timestamp);
        req.Headers.Add("OK-ACCESS-PASSPHRASE", _passphrase);
    }

    private string Sign(string payload)
    {
        var hash = _hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
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
