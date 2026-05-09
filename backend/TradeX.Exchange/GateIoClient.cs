using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public class GateIoClient : IExchangeClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly HMACSHA512 _hmac;

    public ExchangeType Type => ExchangeType.Gate;

    public GateIoClient(string apiKey, string secretKey)
    {
        _apiKey = apiKey;
        _hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secretKey));
        _http = new HttpClient { BaseAddress = new Uri("https://api.gateio.ws") };
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
        var resp = await _http.GetAsync($"/api/v4/spot/candlesticks?currency_pair={Pair}&interval={interval}&from={from}&to={to}&limit=500", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(k =>
        {
            var arr = k.EnumerateArray().ToList();
            return new Candle(
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(arr[0].GetString()!)).UtcDateTime,
                decimal.Parse(arr[1].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[2].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[3].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[4].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(arr[5].GetString()!, CultureInfo.InvariantCulture));
        }).ToArray();
    }

    public async Task<OrderBook> GetOrderBookAsync(string Pair, int limit, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/v4/spot/order_book?currency_pair={Pair}&limit={limit}", ct);
        if (!resp.IsSuccessStatusCode) return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var bids = ParseDepthEntries(doc.RootElement.GetProperty("bids"));
        var asks = ParseDepthEntries(doc.RootElement.GetProperty("asks"));
        return new OrderBook(bids, asks, DateTime.UtcNow);
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signStr = $"GET\n/api/v4/spot/accounts\n\n{EmptyBodyHash}\n{ts}";
        var sign = Sign(signStr);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v4/spot/accounts");
        AddAuthHeaders(req, sign, ts);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gate.io 现货余额查询失败: {(int)resp.StatusCode} {(string.IsNullOrWhiteSpace(errBody) ? "(empty)" : errBody[..Math.Min(errBody.Length, 200)])}");
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var body = Encoding.UTF8.GetString(bytes);
        if (string.IsNullOrWhiteSpace(body)) return [];

        var doc = JsonDocument.Parse(body);
        Dictionary<string, decimal> result = [];
        foreach (var b in doc.RootElement.EnumerateArray())
        {
            var currency = b.GetProperty("currency").GetString()!;
            var available = decimal.Parse(b.GetProperty("available").GetString()!, CultureInfo.InvariantCulture);
            var locked = decimal.Parse(b.GetProperty("locked").GetString()!, CultureInfo.InvariantCulture);
            var total = available + locked;
            if (total > 0) result[currency] = total;
        }
        return result;
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default) => [];

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var side = request.Side == OrderSide.Buy ? "buy" : "sell";
        var bodyDict = new Dictionary<string, string>
        {
            ["currency_pair"] = request.Pair,
            ["side"] = side,
            ["amount"] = request.Quantity.ToString(CultureInfo.InvariantCulture),
            ["type"] = request.Type == OrderType.Limit ? "limit" : "market",
            ["time_in_force"] = "gtc"
        };
        if (request.Price.HasValue)
            bodyDict["price"] = request.Price.Value.ToString(CultureInfo.InvariantCulture)!;

        var resp = await SignedPostAsync("/api/v4/spot/orders", JsonSerializer.Serialize(bodyDict), ct);
        if (resp is null) return new OrderResult(false, null, 0, 0, 0, "请求失败");

        var id = resp.RootElement.GetProperty("id").GetString();
        var label = resp.RootElement.GetProperty("label").GetString();
        if (!string.IsNullOrEmpty(label))
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {label}");

        return new OrderResult(true, id?.ToString(), 0, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var resp = await SignedDeleteAsync($"/api/v4/spot/orders/{exchangeOrderId}", ct);
        if (resp is null) return new OrderResult(false, null, 0, 0, 0, "撤单失败");
        return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var resp = await SignedGetAsync($"/api/v4/spot/orders/{exchangeOrderId}", null, ct);
        if (resp is null) return new OrderResult(false, null, 0, 0, 0, "查询失败");

        var filled = decimal.Parse(resp.RootElement.GetProperty("filled_total").GetString()!, CultureInfo.InvariantCulture);
        return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var from = new DateTimeOffset(since).ToUnixTimeSeconds();
        var query = $"currency_pair=BTCUSDT&from={from}&limit=50";
        var doc = await SignedGetAsync("/api/v4/spot/orders", query, ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(o => new OrderResult(
            o.GetProperty("status").GetString() == "closed",
            o.GetProperty("id").GetString(),
            decimal.Parse(o.GetProperty("filled_total").GetString()!, CultureInfo.InvariantCulture),
            0,
            decimal.Parse(o.GetProperty("fee").GetString()!, CultureInfo.InvariantCulture), null
        )).ToArray();
    }

    private static readonly string EmptyBodyHash = Convert.ToHexStringLower(SHA512.HashData([]));

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        var doc = await SignedGetAsync("/api/v4/spot/open_orders", null, ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(ParseGateOrder).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        var doc = await SignedGetAsync("/api/v4/spot/orders", "status=finished&limit=50", ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(ParseGateOrder).ToArray();
    }

    private static ExchangeOrderDto ParseGateOrder(JsonElement o)
    {
        var side = o.GetProperty("side").GetString();
        var type = o.GetProperty("type").GetString();
        var status = o.GetProperty("status").GetString();
        return new ExchangeOrderDto(
            o.GetProperty("currency_pair").GetString()!,
            side == "buy" || side == "sell" ? side[..1].ToUpper() + side[1..] : side ?? "",
            type == "limit" || type == "market" ? type[..1].ToUpper() + type[1..] : type ?? "",
            status == "open" ? "New" : status == "filled" ? "Filled" : status == "cancelled" ? "Cancelled" : status ?? "",
            decimal.Parse(o.GetProperty("price").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(o.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(o.GetProperty("filled_amount").GetString()!, CultureInfo.InvariantCulture),
            o.GetProperty("id").GetString()!,
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(o.GetProperty("create_time").GetString()!)).UtcDateTime
        );
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signStr = $"GET\n/api/v4/spot/accounts\n\n{EmptyBodyHash}\n{ts}";
            var sign = Sign(signStr);
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/v4/spot/accounts");
            AddAuthHeaders(req, sign, ts);
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                var msg = string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body[..Math.Min(body.Length, 200)];
                return new ConnectionTestResult(false, null, msg);
            }
            return new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}");
        }
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/v4/spot/currency_pairs", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray()
            .Where(s => s.GetProperty("trade_status").GetString() == "tradable")
            .Select(s => new PairRule(
                s.GetProperty("id").GetString()!,
                s.GetProperty("precision").GetInt32(),
                s.GetProperty("amount_precision").GetInt32(),
                decimal.Parse(s.GetProperty("min_quote_amount").GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(s.GetProperty("min_base_amount").GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(s.GetProperty("precision").GetInt32().ToString()),
                1m / (decimal)Math.Pow(10, s.GetProperty("amount_precision").GetInt32())
            )).ToArray();
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/v4/spot/tickers", ct);
        if (!resp.IsSuccessStatusCode) return [];
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return [];
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.EnumerateArray().Select(t => new TickerPrice(
            t.GetProperty("currency_pair").GetString()!,
            TryParseDecimal(t.GetProperty("last").GetString()),
            TryParseDecimal(t.GetProperty("change_percentage").GetString()),
            TryParseDecimal(t.GetProperty("base_volume").GetString()),
            TryParseDecimal(t.GetProperty("high_24h").GetString()),
            TryParseDecimal(t.GetProperty("low_24h").GetString())
        )).ToArray();
    }

    private static decimal TryParseDecimal(string? s)
    {
        if (s is null) return 0;
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
        return v;
    }

    private async Task<JsonDocument?> SignedGetAsync(string path, string? query, CancellationToken ct)
    {
        var url = query is not null ? $"{path}?{query}" : path;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signStr = $"GET\n{path}\n{query ?? ""}\n{EmptyBodyHash}\n{ts}";
        var sign = Sign(signStr);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeaders(req, sign, ts);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private async Task<JsonDocument?> SignedPostAsync(string path, string body, CancellationToken ct)
    {
        var sha512 = Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(body)));
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signStr = $"POST\n{path}\n\n{sha512}\n{ts}";
        var sign = Sign(signStr);

        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        AddAuthHeaders(req, sign, ts);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private async Task<JsonDocument?> SignedDeleteAsync(string path, CancellationToken ct)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signStr = $"DELETE\n{path}\n\n{EmptyBodyHash}\n{ts}";
        var sign = Sign(signStr);

        var req = new HttpRequestMessage(HttpMethod.Delete, path);
        AddAuthHeaders(req, sign, ts);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
    }

    private void AddAuthHeaders(HttpRequestMessage req, string sign, string timestamp)
    {
        req.Headers.Add("KEY", _apiKey);
        req.Headers.Add("SIGN", sign);
        req.Headers.Add("Timestamp", timestamp);
    }

    private string Sign(string payload)
    {
        var hash = _hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
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
