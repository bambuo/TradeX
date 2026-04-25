using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public class HtxClient : IExchangeClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly HMACSHA256 _hmac;

    public ExchangeType Type => ExchangeType.HTX;

    public HtxClient(string apiKey, string secretKey)
    {
        _apiKey = apiKey;
        _hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        _http = new HttpClient { BaseAddress = new Uri("https://api.huobi.pro") };
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
        var size = 500;
        var resp = await _http.GetAsync($"/market/history/kline?symbol={symbol}&period={interval}&size={size}", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("status").GetString() != "ok") return [];

        var data = doc.RootElement.GetProperty("data").EnumerateArray();
        return data.Select(k => new Candle(
            DateTimeOffset.FromUnixTimeSeconds(k.GetProperty("id").GetInt64()).UtcDateTime,
            decimal.Parse(k.GetProperty("open").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k.GetProperty("high").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k.GetProperty("low").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k.GetProperty("close").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k.GetProperty("vol").GetString()!, CultureInfo.InvariantCulture)
        )).ToArray();
    }

    public async Task<OrderBook> GetOrderBookAsync(string symbol, int limit, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/market/depth?symbol={symbol}&type=step0&depth={limit}", ct);
        if (!resp.IsSuccessStatusCode) return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("status").GetString() != "ok")
            return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var tick = doc.RootElement.GetProperty("tick");
        var bids = ParseDepthEntries(tick.GetProperty("bids"));
        var asks = ParseDepthEntries(tick.GetProperty("asks"));
        return new OrderBook(bids, asks, DateTime.UtcNow);
    }

    public async Task<AccountBalance> GetBalanceAsync(CancellationToken ct = default)
    {
        var resp = await SignedGetAsync("/v1/account/accounts", ct);
        if (resp is null) return new AccountBalance(0, 0, 0);

        var accounts = resp.RootElement.GetProperty("data").EnumerateArray();
        var spotAccount = accounts.FirstOrDefault(a => a.GetProperty("type").GetString() == "spot");
        if (spotAccount.ValueKind == JsonValueKind.Undefined) return new AccountBalance(0, 0, 0);

        var accountId = spotAccount.GetProperty("id").GetInt64();
        var balanceResp = await SignedGetAsync($"/v1/account/accounts/{accountId}/balance", ct);
        if (balanceResp is null) return new AccountBalance(0, 0, 0);

        var usdtBalance = balanceResp.RootElement.GetProperty("data").GetProperty("list").EnumerateArray()
            .FirstOrDefault(b => b.GetProperty("currency").GetString() == "usdt");
        if (usdtBalance.ValueKind == JsonValueKind.Undefined) return new AccountBalance(0, 0, 0);

        var trade = usdtBalance.GetProperty("type").GetString() == "trade"
            ? decimal.Parse(usdtBalance.GetProperty("balance").GetString()!, CultureInfo.InvariantCulture)
            : 0;
        var frozen = usdtBalance.GetProperty("type").GetString() == "frozen"
            ? decimal.Parse(usdtBalance.GetProperty("balance").GetString()!, CultureInfo.InvariantCulture)
            : 0;
        return new AccountBalance(trade + frozen, trade, frozen);
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default) => [];

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var body = new
        {
            symbol = request.Symbol,
            type = request.Side == OrderSide.Buy ? "buy-market" : "sell-market",
            amount = request.Quantity.ToString(CultureInfo.InvariantCulture)
        };

        var resp = await SignedPostAsync("/v1/order/orders/place", JsonSerializer.Serialize(body), ct);
        if (resp is null) return new OrderResult(false, null, 0, 0, 0, "请求失败");

        var status = resp.RootElement.GetProperty("status").GetString();
        if (status != "ok")
        {
            var errMsg = resp.RootElement.GetProperty("err-msg").GetString();
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {errMsg}");
        }

        var orderId = resp.RootElement.GetProperty("data").GetString();
        return new OrderResult(true, orderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var resp = await SignedPostAsync($"/v1/order/orders/{exchangeOrderId}/submitcancel", "{}", ct);
        if (resp is null) return new OrderResult(false, null, 0, 0, 0, "撤单失败");
        var status = resp.RootElement.GetProperty("status").GetString();
        return status == "ok"
            ? new OrderResult(true, exchangeOrderId, 0, 0, 0, null)
            : new OrderResult(false, null, 0, 0, 0, "撤单失败");
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var resp = await SignedGetAsync($"/v1/order/orders/{exchangeOrderId}", ct);
        if (resp is null) return new OrderResult(false, null, 0, 0, 0, "查询失败");

        var data = resp.RootElement.GetProperty("data");
        var filled = decimal.Parse(data.GetProperty("field-amount").GetString()!, CultureInfo.InvariantCulture);
        return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var startMs = new DateTimeOffset(since).ToUnixTimeMilliseconds();
        var query = $"symbol={Uri.EscapeDataString("btcusdt")}&states=submitted,partial-filled,partial-canceled,filled,canceled&start-time={startMs}&size=50";
        var doc = await SignedGetAsync($"/v1/order/orders?{query}", ct);
        if (doc is null) return [];

        var status = doc.RootElement.GetProperty("status").GetString();
        if (status != "ok") return [];

        return doc.RootElement.GetProperty("data").EnumerateArray().Select(o => new OrderResult(
            o.GetProperty("state").GetString() == "filled",
            o.GetProperty("id").GetInt64().ToString(),
            decimal.Parse(o.GetProperty("field-amount").GetString()!, CultureInfo.InvariantCulture),
            0,
            decimal.Parse(o.GetProperty("field-fees").GetString()!, CultureInfo.InvariantCulture), null
        )).ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await SignedGetAsync("/v1/account/accounts", ct);
            if (resp is null) return new ConnectionTestResult(false, null, "连接失败");
            var status = resp.RootElement.GetProperty("status").GetString();
            return status == "ok"
                ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful")
                : new ConnectionTestResult(false, null, $"API 错误: {resp.RootElement.GetProperty("err-msg").GetString()}");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}");
        }
    }

    public async Task<SymbolRule[]> GetSymbolRulesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/v1/common/symbols", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("status").GetString() != "ok") return [];

        var data = doc.RootElement.GetProperty("data").EnumerateArray()
            .Where(s => s.GetProperty("state").GetString() == "online");
        return data.Select(s => new SymbolRule(
            s.GetProperty("symbol").GetString()!,
            s.GetProperty("price-precision").GetInt32(),
            s.GetProperty("amount-precision").GetInt32(),
            decimal.Parse(s.GetProperty("min-order-amt").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(s.GetProperty("min-order-amt").GetString()!, CultureInfo.InvariantCulture) / 100,
            s.GetProperty("price-precision").GetInt32(),
            s.GetProperty("amount-precision").GetInt32()
        )).ToArray();
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/market/tickers", ct);
        if (!resp.IsSuccessStatusCode) return [];
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null || doc.RootElement.GetProperty("status").GetString() != "ok") return [];

        return doc.RootElement.GetProperty("data").EnumerateArray().Select(t => new TickerPrice(
            t.GetProperty("symbol").GetString()!,
            TryParseDecimal(t.GetProperty("close").GetString()),
            TryParseDecimal(t.GetProperty("percentChange").GetString()),
            TryParseDecimal(t.GetProperty("vol").GetString()),
            TryParseDecimal(t.GetProperty("high").GetString()),
            TryParseDecimal(t.GetProperty("low").GetString())
        )).ToArray();
    }

    private async Task<JsonDocument?> SignedGetAsync(string path, CancellationToken ct)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var method = "GET";
        var query = $"AccessKeyId={_apiKey}&SignatureMethod=HmacSHA256&SignatureVersion=2&Timestamp={Uri.EscapeDataString(timestamp)}";
        var signStr = $"{method}\napi.huobi.pro\n{path}\n{query}";
        var signature = Sign(signStr);
        var fullQuery = $"{path}?{query}&Signature={Uri.EscapeDataString(signature)}";

        var req = new HttpRequestMessage(HttpMethod.Get, fullQuery);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private async Task<JsonDocument?> SignedPostAsync(string path, string body, CancellationToken ct)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var method = "POST";
        var query = $"AccessKeyId={_apiKey}&SignatureMethod=HmacSHA256&SignatureVersion=2&Timestamp={Uri.EscapeDataString(timestamp)}";
        var signStr = $"{method}\napi.huobi.pro\n{path}\n{query}";
        var signature = Sign(signStr);
        var fullPath = $"{path}?{query}&Signature={Uri.EscapeDataString(signature)}";

        var req = new HttpRequestMessage(HttpMethod.Post, fullPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Content-Type", "application/json");

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private string Sign(string payload)
    {
        var hash = _hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
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
