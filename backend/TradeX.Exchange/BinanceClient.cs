using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public class BinanceClient : IExchangeClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly HMACSHA256 _hmac;

    public ExchangeType Type => ExchangeType.Binance;

    public BinanceClient(string apiKey, string secretKey, bool isTestnet)
    {
        _apiKey = apiKey;
        _hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        _http = new HttpClient
        {
            BaseAddress = new Uri(isTestnet ? "https://testnet.binance.vision" : "https://api.binance.com")
        };
    }

    public async IAsyncEnumerable<Candle> SubscribeKlinesAsync(string symbol, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastTime = 0L;
        while (!ct.IsCancellationRequested)
        {
            var candles = await FetchKlinesAsync(symbol, interval, lastTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastTime).UtcDateTime : DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, ct);
            foreach (var c in candles)
                if (new DateTimeOffset(c.Timestamp).ToUnixTimeMilliseconds() > lastTime)
                {
                    lastTime = new DateTimeOffset(c.Timestamp).ToUnixTimeMilliseconds();
                    yield return c;
                }
            await Task.Delay(1000, ct);
        }
    }

    public async Task<Candle[]> GetKlinesAsync(string symbol, string interval, DateTime start, DateTime end, CancellationToken ct = default)
        => (await FetchKlinesAsync(symbol, interval, start, end, ct)).ToArray();

    public async Task<OrderBook> GetOrderBookAsync(string symbol, int limit, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/v3/depth?symbol={symbol}&limit={limit}", ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

        var bids = ParseDepth(doc.RootElement.GetProperty("bids"));
        var asks = ParseDepth(doc.RootElement.GetProperty("asks"));
        return new OrderBook(bids, asks, DateTime.UtcNow);
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        var query = $"timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/account?{query}&signature={Sign(query)}");
        request.Headers.Add("X-MBX-APIKEY", _apiKey);

        var resp = await _http.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        Dictionary<string, decimal> result = [];
        foreach (var b in doc.RootElement.GetProperty("balances").EnumerateArray())
        {
            var asset = b.GetProperty("asset").GetString()!;
            var free = decimal.Parse(b.GetProperty("free").GetString()!, CultureInfo.InvariantCulture);
            var locked = decimal.Parse(b.GetProperty("locked").GetString()!, CultureInfo.InvariantCulture);
            var total = free + locked;
            if (total > 0) result[asset] = total;
        }
        return result;
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default)
    {
        var query = $"timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/account?{query}&signature={Sign(query)}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        var balances = doc.RootElement.GetProperty("balances").EnumerateArray()
            .Where(b => decimal.Parse(b.GetProperty("free").GetString()!, CultureInfo.InvariantCulture) > 0
                     || decimal.Parse(b.GetProperty("locked").GetString()!, CultureInfo.InvariantCulture) > 0)
            .Select(b => new ExchangePosition(
                b.GetProperty("asset").GetString()! + "USDT",
                decimal.Parse(b.GetProperty("free").GetString()!, CultureInfo.InvariantCulture)
                    + decimal.Parse(b.GetProperty("locked").GetString()!, CultureInfo.InvariantCulture),
                0, 0, 0))
            .ToArray();

        return balances;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var side = request.Side == OrderSide.Buy ? "BUY" : "SELL";
        var type = request.Type switch
        {
            OrderType.Limit => "LIMIT",
            OrderType.StopLimit => "STOP_LOSS_LIMIT",
            _ => "MARKET"
        };
        var query = $"symbol={request.Symbol}&side={side}&type={type}&quantity={request.Quantity}&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        if (request.Price.HasValue) query += $"&price={request.Price}";
        if (request.StopPrice.HasValue) query += $"&stopPrice={request.StopPrice}";
        if (type == "LIMIT" || type == "STOP_LOSS_LIMIT") query += "&timeInForce=GTC";
        query += $"&signature={Sign(query)}";

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v3/order?{query}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {resp.StatusCode}");

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "空响应");

        var orderId = doc.RootElement.GetProperty("orderId").GetInt64().ToString();
        var filled = decimal.Parse(doc.RootElement.GetProperty("executedQty").GetString()!, CultureInfo.InvariantCulture);
        return new OrderResult(true, orderId, filled, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var query = $"symbol=BTCUSDT&orderId={exchangeOrderId}&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&signature={Sign($"symbol=BTCUSDT&orderId={exchangeOrderId}&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}")}";
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v3/order?{query}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return new OrderResult(false, null, 0, 0, 0, "撤单失败");
        return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var query = $"symbol=BTCUSDT&orderId={exchangeOrderId}&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/order?{query}&signature={Sign(query)}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return new OrderResult(false, null, 0, 0, 0, "查询订单失败");
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return new OrderResult(false, null, 0, 0, 0, "空响应");
        var filled = decimal.Parse(doc.RootElement.GetProperty("executedQty").GetString()!, CultureInfo.InvariantCulture);
        return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var startMs = new DateTimeOffset(since).ToUnixTimeMilliseconds();
        var query = $"symbol=BTCUSDT&startTime={startMs}&limit=50&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/allOrders?{query}&signature={Sign(query)}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(o => new OrderResult(
            o.GetProperty("status").GetString() == "FILLED",
            o.GetProperty("orderId").GetInt64().ToString(),
            decimal.Parse(o.GetProperty("executedQty").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(o.GetProperty("cummulativeQuoteQty").GetString()!, CultureInfo.InvariantCulture),
            0,
            null
        )).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        var query = $"timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/openOrders?{query}&signature={Sign(query)}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(o => ParseBinanceOrder(o)).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        var symbols = new HashSet<string>();

        var assets = await GetAssetBalancesAsync(ct);
        foreach (var asset in assets.Keys)
            if (asset != "USDT" && !string.IsNullOrWhiteSpace(asset))
                symbols.Add($"{asset}USDT");

        symbols.Add("BTCUSDT");
        symbols.Add("ETHUSDT");

        var results = new List<ExchangeOrderDto>();

        foreach (var symbol in symbols)
        {
            var query = $"symbol={symbol}&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&limit=20";
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/allOrders?{query}&signature={Sign(query)}");
            req.Headers.Add("X-MBX-APIKEY", _apiKey);
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) continue;

            var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
            if (doc is null) continue;

            foreach (var o in doc.RootElement.EnumerateArray())
                results.Add(ParseBinanceOrder(o));
        }

        return [.. results.OrderByDescending(r => r.PlacedAt)];
    }

    private static ExchangeOrderDto ParseBinanceOrder(JsonElement o)
    {
        return new ExchangeOrderDto(
            o.GetProperty("symbol").GetString()!,
            o.GetProperty("side").GetString() == "BUY" ? "Buy" : "Sell",
            o.GetProperty("type").GetString() switch { "LIMIT" => "Limit", "STOP_LOSS_LIMIT" => "StopLimit", _ => "Market" },
            o.GetProperty("status").GetString() switch
            {
                "NEW" => "New",
                "PARTIALLY_FILLED" => "PartiallyFilled",
                "FILLED" => "Filled",
                "CANCELED" => "Cancelled",
                "EXPIRED" => "Expired",
                _ => o.GetProperty("status").GetString()!
            },
            decimal.Parse(o.GetProperty("price").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(o.GetProperty("origQty").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(o.GetProperty("executedQty").GetString()!, CultureInfo.InvariantCulture),
            o.GetProperty("orderId").GetInt64().ToString(),
            DateTimeOffset.FromUnixTimeMilliseconds(o.GetProperty("time").GetInt64()).UtcDateTime
        );
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var ping = await _http.GetAsync("/api/v3/ping", ct);
        if (!ping.IsSuccessStatusCode)
            return new ConnectionTestResult(false, null, "连接失败");

        var query = $"timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/account?{query}&signature={Sign(query)}");
        req.Headers.Add("X-MBX-APIKEY", _apiKey);
        var acct = await _http.SendAsync(req, ct);
        if (!acct.IsSuccessStatusCode)
            return new ConnectionTestResult(true, new() { ["spotTrade"] = false }, "Ping 成功但 API Key 无权限");

        var doc = await acct.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var canWithdraw = doc?.RootElement.GetProperty("canWithdraw").GetBoolean() ?? true;
        return new ConnectionTestResult(true, new()
        {
            ["spotTrade"] = true,
            ["withdrawDisabled"] = !canWithdraw,
            ["ipWhitelistRecommended"] = true
        }, "Connection successful. Spot trade permission verified.");
    }

    public async Task<SymbolRule[]> GetSymbolRulesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/v3/exchangeInfo", ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        return doc.RootElement.GetProperty("symbols").EnumerateArray()
            .Where(s => s.GetProperty("status").GetString() == "TRADING" && s.GetProperty("isSpotTradingAllowed").GetBoolean())
            .Select(s => new SymbolRule(
                s.GetProperty("symbol").GetString()!,
                s.GetProperty("quotePrecision").GetInt32(),
                s.GetProperty("baseAssetPrecision").GetInt32(),
                ParseFilter(s, "MIN_NOTIONAL", "minNotional"),
                ParseFilter(s, "LOT_SIZE", "minQty"),
                ParseFilter(s, "PRICE_FILTER", "tickSize"),
                ParseFilter(s, "LOT_SIZE", "stepSize")))
            .ToArray();
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/v3/ticker/24hr", ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(t => new TickerPrice(
            t.GetProperty("symbol").GetString()!,
            decimal.Parse(t.GetProperty("lastPrice").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(t.GetProperty("priceChangePercent").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(t.GetProperty("volume").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(t.GetProperty("highPrice").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(t.GetProperty("lowPrice").GetString()!, CultureInfo.InvariantCulture)
        )).ToArray();
    }

    private string Sign(string query)
    {
        var hash = _hmac.ComputeHash(Encoding.UTF8.GetBytes(query));
        return Convert.ToHexStringLower(hash);
    }

    private static decimal[,] ParseDepth(JsonElement entries)
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

    private async Task<List<Candle>> FetchKlinesAsync(string symbol, string interval, DateTime start, DateTime end, CancellationToken ct)
    {
        var startMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();
        var resp = await _http.GetAsync($"/api/v3/klines?symbol={symbol}&interval={interval}&startTime={startMs}&endTime={endMs}&limit=1000", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
        if (doc is null) return [];

        return doc.RootElement.EnumerateArray().Select(k => new Candle(
            DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()).UtcDateTime,
            decimal.Parse(k[1].GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k[2].GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k[3].GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k[4].GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(k[5].GetString()!, CultureInfo.InvariantCulture)
        )).ToList();
    }

    private static decimal ParseFilter(JsonElement symbol, string filterType, string field)
    {
        foreach (var f in symbol.GetProperty("filters").EnumerateArray())
            if (f.GetProperty("filterType").GetString() == filterType && f.TryGetProperty(field, out var val))
                return decimal.Parse(val.GetString()!, CultureInfo.InvariantCulture);
        return 0;
    }
}
