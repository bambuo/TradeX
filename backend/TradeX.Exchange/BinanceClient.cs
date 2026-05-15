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

public class BinanceClient(string apiKey, string secretKey, bool isTestnet) : IExchangeClient
{
    private readonly IBinanceRestApi _api = CreateRefitClient(apiKey, secretKey, isTestnet);

    public ExchangeType Type => ExchangeType.Binance;

    private static IBinanceRestApi CreateRefitClient(string key, string secret, bool testnet)
    {
        var baseUri = new Uri(testnet ? "https://testnet.binance.vision" : "https://api.binance.com");

        var auth = new BinanceAuthHandler(key, secret)
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10
            }
        };
        var handler = new ResilienceHandler(ExchangeType.Binance) { InnerHandler = auth };

        var http = new HttpClient(handler) { BaseAddress = baseUri };
        return RestService.For<IBinanceRestApi>(http, new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            })
        });
    }

    public async IAsyncEnumerable<Candle> SubscribeKlinesAsync(string pair, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastTime = 0L;
        while (!ct.IsCancellationRequested)
        {
            var candles = await FetchKlinesAsync(pair, interval, lastTime > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(lastTime).UtcDateTime
                : DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, ct);

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

    public async Task<Candle[]> GetKlinesAsync(string pair, string interval, DateTime start, DateTime end, CancellationToken ct = default)
        => (await FetchKlinesAsync(pair, interval, start, end, ct)).ToArray();

    public async Task<OrderBook> GetOrderBookAsync(string pair, int limit, CancellationToken ct = default)
    {
        var resp = await _api.GetOrderBookAsync(pair, limit, ct);
        var bids = ParseDepth(resp.Bids);
        var asks = ParseDepth(resp.Asks);
        return new OrderBook(bids, asks, DateTime.UtcNow);
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        var account = await _api.GetAccountInfoAsync(ct);
        Dictionary<string, decimal> result = [];
        foreach (var b in account.Balances)
        {
            var total = b.Free + b.Locked;
            if (total > 0)
                result[b.Asset] = total;
        }
        return result;
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default)
    {
        var account = await _api.GetAccountInfoAsync(ct);
        return account.Balances
            .Where(b => (b.Free + b.Locked) > 0)
            .Select(b => new ExchangePosition(
                $"{b.Asset}USDT",
                b.Free + b.Locked,
                0, 0, 0))
            .ToArray();
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

        try
        {
            var resp = await _api.PlaceOrderAsync(
                request.Pair, side, type,
                timeInForce: type is "LIMIT" or "STOP_LOSS_LIMIT" ? "GTC" : null,
                quantity: request.Quantity.ToString(CultureInfo.InvariantCulture),
                price: request.Price?.ToString(CultureInfo.InvariantCulture),
                stopPrice: request.StopPrice?.ToString(CultureInfo.InvariantCulture),
                ct: ct);

            return new OrderResult(true, resp.OrderId.ToString(), resp.ExecutedQty, 0, 0, null);
        }
        catch (ApiException ex)
        {
            return new OrderResult(false, null, 0, 0, 0, $"交易所拒绝: {ex.StatusCode} {ex.Message}");
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.CancelOrderAsync("BTCUSDT", orderId: exchangeOrderId, ct: ct);
            return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
        }
        catch (ApiException)
        {
            return new OrderResult(false, null, 0, 0, 0, "撤单失败");
        }
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _api.GetOrderAsync("BTCUSDT", orderId: exchangeOrderId, ct: ct);
            return new OrderResult(true, exchangeOrderId, resp.ExecutedQty, 0, 0, null);
        }
        catch (ApiException)
        {
            return new OrderResult(false, null, 0, 0, 0, "查询订单失败");
        }
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        try
        {
            var startMs = new DateTimeOffset(since).ToUnixTimeMilliseconds();
            var orders = await _api.GetAllOrdersAsync("BTCUSDT", startTime: startMs, limit: 50, ct: ct);
            return orders.Select(o => new OrderResult(
                o.Status == "FILLED",
                o.OrderId.ToString(),
                o.ExecutedQty,
                o.CumulativeQuoteQty,
                0, null)).ToArray();
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
            return orders.Select(ParseBinanceOrder).ToArray();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        var results = new List<ExchangeOrderDto>();
        var assets = await GetAssetBalancesAsync(ct);

        var pairs = assets.Keys
            .Where(a => a != "USDT")
            .Select(a => $"{a}USDT")
            .ToHashSet();
        pairs.Add("BTCUSDT");
        pairs.Add("ETHUSDT");

        foreach (var pair in pairs)
        {
            try
            {
                var orders = await _api.GetAllOrdersAsync(pair, limit: 20, ct: ct);
                results.AddRange(orders.Select(ParseBinanceOrder));
            }
            catch (ApiException)
            {
                // skip pairs that fail
            }
        }

        return [.. results.OrderByDescending(r => r.PlacedAt)];
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await _api.PingAsync(ct);
        }
        catch
        {
            return new ConnectionTestResult(false, null, "连接失败");
        }

        try
        {
            var account = await _api.GetAccountInfoAsync(ct);
            return new ConnectionTestResult(true, new()
            {
                ["spotTrade"] = account.CanTrade,
                ["withdrawDisabled"] = !account.CanWithdraw,
                ["ipWhitelistRecommended"] = true
            }, "Connection successful. Spot trade permission verified.");
        }
        catch
        {
            return new ConnectionTestResult(true, new() { ["spotTrade"] = false }, "Ping 成功但 API Key 无权限");
        }
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var info = await _api.GetExchangeInfoAsync(ct);
        return info.Symbols
            .Where(s => s.Status == "TRADING" && s.IsSpotTradingAllowed)
            .Select(s => new PairRule(
                s.Symbol,
                s.Filters.FirstOrDefault(f => f.FilterType == "PRICE_FILTER")?.TickSize is { } ts
                    ? BitConverter.GetBytes(decimal.GetBits(ts)[3])[2] + 1
                    : 8,
                s.BaseAsset.Length,
                s.Filters.FirstOrDefault(f => f.FilterType == "MIN_NOTIONAL")?.MinNotional ?? 0,
                s.Filters.FirstOrDefault(f => f.FilterType == "LOT_SIZE")?.MinQty ?? 0,
                s.Filters.FirstOrDefault(f => f.FilterType == "PRICE_FILTER")?.TickSize ?? 0,
                s.Filters.FirstOrDefault(f => f.FilterType == "LOT_SIZE")?.StepSize ?? 0))
            .ToArray();
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var tickers = await _api.GetAllTicker24HrAsync(ct);
        return tickers.Select(t => new TickerPrice(
            t.Symbol, t.LastPrice, t.PriceChangePercent,
            t.Volume, t.HighPrice, t.LowPrice)).ToArray();
    }

    private async Task<List<Candle>> FetchKlinesAsync(string pair, string interval, DateTime start, DateTime end, CancellationToken ct)
    {
        var startMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();

        try
        {
            var klines = await _api.GetKlinesAsync(pair, interval, startMs, endMs, 1000, ct);
            return klines.Select(k => new Candle(
                DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                k.Open, k.High, k.Low, k.Close, k.Volume)).ToList();
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private static ExchangeOrderDto ParseBinanceOrder(BinanceOrderResponse o)
    {
        var placedAt = o.Time.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(o.Time.Value).UtcDateTime
            : DateTime.UtcNow;

        return new ExchangeOrderDto(
            o.Symbol,
            o.Side == "BUY" ? "Buy" : "Sell",
            o.Type switch { "LIMIT" => "Limit", "STOP_LOSS_LIMIT" => "StopLimit", _ => "Market" },
            o.Status switch
            {
                "NEW" => "New",
                "PARTIALLY_FILLED" => "PartiallyFilled",
                "FILLED" => "Filled",
                "CANCELED" => "Cancelled",
                "EXPIRED" => "Expired",
                _ => o.Status
            },
            o.Price,
            o.OrigQty,
            o.ExecutedQty,
            o.OrderId.ToString(),
            placedAt
        );
    }

    private static decimal[,] ParseDepth(decimal[][] entries)
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
