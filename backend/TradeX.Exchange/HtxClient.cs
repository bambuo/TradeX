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

public class HtxClient(string apiKey, string secretKey) : IExchangeClient
{
    private readonly IHtxRestApi _api = CreateRefitClient(apiKey, secretKey);
    private long? _spotAccountId;

    public ExchangeType Type => ExchangeType.HTX;

    private static IHtxRestApi CreateRefitClient(string key, string secret)
    {
        var auth = new HtxAuthHandler(key, secret)
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10
            }
        };
        var handler = new ResilienceHandler(ExchangeType.HTX) { InnerHandler = auth };

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.huobi.pro") };
        return RestService.For<IHtxRestApi>(http, new RefitSettings
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
        try
        {
            var resp = await _api.GetKlinesAsync(Pair, interval, 500, ct);
            if (resp.Status != "ok") return [];

            return resp.Data.Select(k => new Candle(
                DateTimeOffset.FromUnixTimeSeconds(k.Id).UtcDateTime,
                decimal.Parse(k.Open, CultureInfo.InvariantCulture),
                decimal.Parse(k.High, CultureInfo.InvariantCulture),
                decimal.Parse(k.Low, CultureInfo.InvariantCulture),
                decimal.Parse(k.Close, CultureInfo.InvariantCulture),
                decimal.Parse(k.Vol, CultureInfo.InvariantCulture))).ToArray();
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
            var resp = await _api.GetOrderBookAsync(Pair, "step0", limit, ct);
            if (resp.Status != "ok")
                return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

            var bids = ParseDepthEntries(resp.Data.Bids);
            var asks = ParseDepthEntries(resp.Data.Asks);
            return new OrderBook(bids, asks, DateTime.UtcNow);
        }
        catch (ApiException)
        {
            return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);
        }
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        var accountId = await GetSpotAccountIdAsync(ct);
        if (accountId is null) return [];

        try
        {
            var resp = await _api.GetAccountBalanceAsync(accountId.Value, ct);
            if (resp.Status != "ok" || resp.Data is null) return [];

            Dictionary<string, decimal> result = [];
            foreach (var entry in resp.Data.List)
            {
                var currency = entry.Currency.ToUpperInvariant();
                var balance = decimal.Parse(entry.Balance, CultureInfo.InvariantCulture);
                if (result.TryGetValue(currency, out var existing))
                    result[currency] = existing + balance;
                else
                    result[currency] = balance;
            }
            return result.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default) => [];

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var accountId = await GetSpotAccountIdAsync(ct);
        if (accountId is null)
            return new OrderResult(false, null, 0, 0, 0, "无法获取账户 ID");

        var type = request.Side == OrderSide.Buy
            ? (request.Type == OrderType.Limit ? "buy-limit" : "buy-market")
            : (request.Type == OrderType.Limit ? "sell-limit" : "sell-market");

        var body = new HtxPlaceOrderRequest(
            accountId.Value.ToString(),
            request.Pair.ToLowerInvariant(),
            type,
            request.Quantity.ToString(CultureInfo.InvariantCulture),
            request.Price?.ToString(CultureInfo.InvariantCulture),
            // HTX client-order-id ≤64 chars；GUID N (32) 充分够用
            ClientOrderId: request.ClientOrderId);

        try
        {
            var resp = await _api.PlaceOrderAsync(body, ct);
            if (resp.Status != "ok")
                return new OrderResult(false, null, 0, 0, 0, "交易所拒绝");

            return new OrderResult(true, resp.Data, 0, 0, 0, null);
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {ex.Message}");
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        // HTX 撤单只需 orderId（pair 用于业务上一致性，未来日志/告警保留）
        try
        {
            var resp = await _api.CancelOrderAsync(exchangeOrderId, ct);
            return resp.Status == "ok"
                ? new OrderResult(true, exchangeOrderId, 0, 0, 0, null)
                : new OrderResult(false, null, 0, 0, 0, "撤单失败");
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"撤单失败: {ex.Message}");
        }
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetClientOrderAsync(clientOrderId, ct);
            if (resp.Status != "ok" || resp.Data is null)
                return new OrderResult(false, null, 0, 0, 0, $"交易所无此 ClientOrderId: {resp.Status}");
            var d = resp.Data;
            var filled = decimal.TryParse(d.FilledAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 0;
            var fee = decimal.TryParse(d.FieldFees, NumberStyles.Any, CultureInfo.InvariantCulture, out var fee0) ? fee0 : 0;
            return new OrderResult(true, d.Id.ToString(), filled, 0, fee, null);
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"按 ClientOrderId 查询失败: {ex.StatusCode} {ex.Message}");
        }
    }

    public async Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetOrderAsync(exchangeOrderId, ct);
            if (resp.Status != "ok" || resp.Data is null)
                return new OrderResult(false, null, 0, 0, 0, "查询失败");

            var filled = decimal.Parse(resp.Data.FilledAmount, CultureInfo.InvariantCulture);
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
            var resp = await _api.GetOrdersAsync(
                symbol: "btcusdt",
                states: "submitted,partial-filled,partial-canceled,filled,canceled",
                startTime: startMs,
                size: 50,
                ct: ct);

            if (resp.Status != "ok") return [];

            return resp.Data.Select(o => new OrderResult(
                o.State == "filled",
                o.Id.ToString(),
                decimal.Parse(o.FilledAmount, CultureInfo.InvariantCulture),
                0,
                decimal.Parse(o.FieldFees, CultureInfo.InvariantCulture),
                null)).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        var accountId = await GetSpotAccountIdAsync(ct);
        if (accountId is null) return [];

        try
        {
            var resp = await _api.GetOpenOrdersAsync(accountId, size: 100, ct: ct);
            if (resp.Status != "ok") return [];

            return resp.Data.Select(ParseHtxOrder).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        var accountId = await GetSpotAccountIdAsync(ct);
        if (accountId is null) return [];

        try
        {
            var resp = await _api.GetOrdersAsync(
                states: "filled,partial-filled,canceled",
                size: 50,
                ct: ct);

            if (resp.Status != "ok") return [];

            return resp.Data.Select(ParseHtxOrder).ToArray();
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
            var resp = await _api.GetAccountsAsync(ct);
            return resp.Status == "ok"
                ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful")
                : new ConnectionTestResult(false, null, "API 错误");
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
            var resp = await _api.GetSymbolsAsync(ct);
            if (resp.Status != "ok") return [];

            return resp.Data
                .Where(s => s.State == "online")
                .Select(s => new PairRule(
                    s.Symbol,
                    s.PricePrecision,
                    s.AmountPrecision,
                    decimal.Parse(s.MinOrderAmt, CultureInfo.InvariantCulture),
                    decimal.Parse(s.MinOrderAmt, CultureInfo.InvariantCulture) / 100,
                    s.PricePrecision,
                    s.AmountPrecision))
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
            var resp = await _api.GetTickersAsync(ct);
            if (resp.Status != "ok") return [];

            return resp.Data.Select(t => new TickerPrice(
                t.Symbol,
                TryParseDecimal(t.Close),
                TryParseDecimal(t.PercentChange),
                TryParseDecimal(t.Vol),
                TryParseDecimal(t.High),
                TryParseDecimal(t.Low))).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private async Task<long?> GetSpotAccountIdAsync(CancellationToken ct)
    {
        if (_spotAccountId.HasValue)
            return _spotAccountId;

        try
        {
            var resp = await _api.GetAccountsAsync(ct);
            if (resp.Status != "ok") return null;

            var spot = resp.Data.FirstOrDefault(a => a.Type == "spot" && a.State == "working");
            if (spot is null) return null;

            _spotAccountId = spot.Id;
            return _spotAccountId;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private static ExchangeOrderDto ParseHtxOrder(HtxOrderDetail o)
    {
        var side = o.Type.StartsWith("buy") ? "Buy" : "Sell";
        var orderType = o.Type.Contains("limit") ? "Limit" : o.Type.Contains("market") ? "Market" : o.Type;

        return new ExchangeOrderDto(
            o.Symbol,
            side,
            orderType,
            o.State switch
            {
                "submitted" => "New",
                "partial-filled" => "PartiallyFilled",
                "filled" => "Filled",
                "canceled" => "Cancelled",
                _ => o.State
            },
            decimal.Parse(o.Price, CultureInfo.InvariantCulture),
            decimal.Parse(o.Amount, CultureInfo.InvariantCulture),
            decimal.Parse(o.FilledAmount, CultureInfo.InvariantCulture),
            o.Id.ToString(),
            DateTimeOffset.FromUnixTimeMilliseconds(o.CreatedAt).UtcDateTime
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
