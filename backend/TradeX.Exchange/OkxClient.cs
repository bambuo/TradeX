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

public class OkxClient(string apiKey, string secretKey, string? passphrase = null) : IExchangeClient
{
    private readonly IOkxRestApi _api = CreateRefitClient(apiKey, secretKey, passphrase ?? "");

    public ExchangeType Type => ExchangeType.OKX;

    private static IOkxRestApi CreateRefitClient(string key, string secret, string pass)
    {
        var auth = new OkxAuthHandler(key, secret, pass)
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10
            }
        };
        var handler = new ResilienceHandler(ExchangeType.OKX) { InnerHandler = auth };

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://www.okx.com") };
        return RestService.For<IOkxRestApi>(http, new RefitSettings
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
        var after = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var before = new DateTimeOffset(end).ToUnixTimeMilliseconds();

        try
        {
            var resp = await _api.GetKlinesAsync(Pair, interval, after, before, 300, ct);
            if (resp.Code != "0") return [];

            return resp.Data.Select(k => new Candle(
                DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(k.Ts)).UtcDateTime,
                decimal.Parse(k.O, CultureInfo.InvariantCulture),
                decimal.Parse(k.H, CultureInfo.InvariantCulture),
                decimal.Parse(k.L, CultureInfo.InvariantCulture),
                decimal.Parse(k.C, CultureInfo.InvariantCulture),
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
            var resp = await _api.GetOrderBookAsync(Pair, limit, ct);
            if (resp.Code != "0" || resp.Data.Count == 0)
                return new OrderBook(new decimal[0, 2], new decimal[0, 2], DateTime.UtcNow);

            var book = resp.Data[0];
            var bids = ParseDepthEntries(book.Bids);
            var asks = ParseDepthEntries(book.Asks);
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
            var resp = await _api.GetAccountBalanceAsync(ct);
            if (resp.Code != "0" || resp.Data.Count == 0) return [];

            Dictionary<string, decimal> result = [];
            foreach (var detail in resp.Data[0].Details)
            {
                var cashBal = decimal.Parse(detail.CashBal, CultureInfo.InvariantCulture);
                if (cashBal > 0)
                    result[detail.Ccy] = cashBal;
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
        var ordType = request.Type == OrderType.Limit ? "limit" : "market";
        var body = new OkxPlaceOrderRequest(
            request.Pair, "cash", side, ordType,
            request.Quantity.ToString(CultureInfo.InvariantCulture),
            request.Price?.ToString(CultureInfo.InvariantCulture),
            // OKX clOrdId 限制：字母数字下划线 ≤32 字符；GUID N 格式 (32 hex) 符合
            ClOrdId: request.ClientOrderId);

        try
        {
            var resp = await _api.PlaceOrderAsync(body, ct);
            if (resp.Code != "0")
            {
                var msg = resp.Data.Count > 0 ? resp.Data[0].SMsg : resp.Msg;
                return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {msg}");
            }

            var orderId = resp.Data[0].OrdId;
            return new OrderResult(true, orderId, 0, 0, 0, null);
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {ex.Message}");
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        var body = new OkxCancelOrderRequest("BTCUSDT", exchangeOrderId);
        try
        {
            var resp = await _api.CancelOrderAsync(body, ct);
            return resp.Code == "0"
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
            var resp = await _api.GetOrderAsync(pair, clOrdId: clientOrderId, ct: ct);
            if (resp.Code != "0" || resp.Data.Count == 0)
                return new OrderResult(false, null, 0, 0, 0, $"交易所无此 ClOrdId: {resp.Msg}");

            var data = resp.Data[0];
            var filled = decimal.Parse(data.AccFillSz, CultureInfo.InvariantCulture);
            var fee = decimal.TryParse(data.Fee, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? Math.Abs(f) : 0;
            return new OrderResult(true, data.OrdId, filled, 0, fee, null);
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
            // TODO: instId 硬编码 "BTCUSDT" 是历史遗留；调用方应传入 pair
            var resp = await _api.GetOrderAsync("BTCUSDT", ordId: exchangeOrderId, ct: ct);
            if (resp.Code != "0" || resp.Data.Count == 0)
                return new OrderResult(false, null, 0, 0, 0, "订单不存在");

            var data = resp.Data[0];
            var filled = decimal.Parse(data.AccFillSz, CultureInfo.InvariantCulture);
            return new OrderResult(true, exchangeOrderId, filled, 0, 0, null);
        }
        catch (ApiException)
        {
            return new OrderResult(false, null, 0, 0, 0, "查询失败");
        }
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var after = new DateTimeOffset(since).ToUnixTimeMilliseconds();
        try
        {
            var resp = await _api.GetOrderHistoryAsync("SPOT", after: after.ToString(), limit: 50, ct: ct);
            if (resp.Code != "0") return [];

            return resp.Data.Select(o => new OrderResult(
                o.State == "filled",
                o.OrdId,
                decimal.Parse(o.AccFillSz, CultureInfo.InvariantCulture),
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
            var resp = await _api.GetPendingOrdersAsync(instType: "SPOT", ct: ct);
            if (resp.Code != "0") return [];

            return resp.Data.Select(ParseOkxOrder).ToArray();
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
            var resp = await _api.GetOrderHistoryAsync("SPOT", limit: 50, ct: ct);
            if (resp.Code != "0") return [];

            return resp.Data.Select(ParseOkxOrder).ToArray();
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
            var resp = await _api.GetAccountBalanceAsync(ct);
            return resp.Code == "0"
                ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "Connection successful")
                : new ConnectionTestResult(false, null, $"API 错误: {resp.Msg}");
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
            var resp = await _api.GetInstrumentsAsync("SPOT", ct);
            if (resp.Code != "0") return [];

            return resp.Data
                .Where(s => s.State == "live")
                .Select(s => new PairRule(
                    s.InstId,
                    s.TickSz.Length - 1,
                    s.LotSz.Length - 1,
                    decimal.Parse(s.MinSz, CultureInfo.InvariantCulture),
                    decimal.Parse(s.MinSz, CultureInfo.InvariantCulture),
                    decimal.Parse(s.TickSz, CultureInfo.InvariantCulture),
                    decimal.Parse(s.LotSz, CultureInfo.InvariantCulture)))
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
            var resp = await _api.GetTickersAsync("SPOT", ct);
            if (resp.Code != "0") return [];

            return resp.Data.Select(t => new TickerPrice(
                t.InstId,
                TryParseDecimal(t.Last),
                TryParseDecimal(t.ChangePercent),
                TryParseDecimal(t.VolCcy24h),
                TryParseDecimal(t.High24h),
                TryParseDecimal(t.Low24h))).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private static ExchangeOrderDto ParseOkxOrder(OkxOrderDetails o)
    {
        return new ExchangeOrderDto(
            o.InstId,
            o.Side == "buy" ? "Buy" : "Sell",
            o.OrdType switch { "limit" => "Limit", "market" => "Market", _ => o.OrdType },
            o.State switch
            {
                "live" => "New",
                "partially_filled" => "PartiallyFilled",
                "filled" => "Filled",
                "cancelled" => "Cancelled",
                _ => o.State
            },
            decimal.Parse(o.Px, CultureInfo.InvariantCulture),
            decimal.Parse(o.Sz, CultureInfo.InvariantCulture),
            decimal.Parse(o.AccFillSz, CultureInfo.InvariantCulture),
            o.OrdId,
            DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(o.CTime)).UtcDateTime
        );
    }

    private static decimal TryParseDecimal(string? s)
    {
        if (s is null) return 0;
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
        return v;
    }

    private static decimal[,] ParseDepthEntries(decimal[][] entries)
    {
        var result = new decimal[entries.Length, 2];
        for (var i = 0; i < entries.Length; i++)
        {
            result[i, 0] = entries[i][0];
            result[i, 1] = entries[i][1];
        }
        return result;
    }
}
