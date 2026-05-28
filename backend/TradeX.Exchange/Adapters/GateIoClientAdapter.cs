using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GateIo.Net;
using GateIo.Net.Clients;
using GateIo.Net.Enums;
using GateIo.Net.Objects.Models;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using ExchangeType = TradeX.Core.Enums.ExchangeType;
using OrderSide = TradeX.Core.Enums.OrderSide;
using OrderType = TradeX.Core.Enums.OrderType;

namespace TradeX.Exchange.Adapters;

public class GateIoClientAdapter : IExchangeClient
{
    private readonly GateIoRestClient _client;
    private readonly bool _hasCredentials;

    public ExchangeType Type => ExchangeType.Gate;

    public GateIoClientAdapter(string apiKey, string secretKey)
    {
        _hasCredentials = !string.IsNullOrWhiteSpace(apiKey);
        _client = new GateIoRestClient(o => { if (_hasCredentials) o.ApiCredentials = new GateIoCredentials(apiKey, secretKey); });
    }

    public async IAsyncEnumerable<Candle> SubscribeKlinesAsync(string pair, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var c = await GetKlinesAsync(pair, interval, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, ct);
            foreach (var x in c) yield return x;
            await Task.Delay(1000, ct);
        }
    }

    public async IAsyncEnumerable<Candle> SubscribeKlinesStreamAsync(string pair, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var gatePair = pair.Replace("USDT", "_USDT");
        var socketClient = new GateIoSocketClient();
        var channel = Channel.CreateBounded<Candle>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.SpotApi.SubscribeToKlineUpdatesAsync(gatePair, MapInterval(interval), data =>
        {
            var k = data.Data;
            channel.Writer.TryWrite(new Candle(k.OpenTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.BaseVolume));
        }, ct);

        try
        {
            if (!subResult.Success)
                yield break;

            await foreach (var candle in channel.Reader.ReadAllAsync(ct))
                yield return candle;
        }
        finally
        {
            socketClient.Dispose();
        }
    }

    public async IAsyncEnumerable<Trade> SubscribeTradesAsync(string pair, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var socketClient = new GateIoSocketClient();
        var channel = Channel.CreateBounded<Trade>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var gatePair = pair.Replace("USDT", "_USDT");
        var subResult = await socketClient.SpotApi.SubscribeToTradeUpdatesAsync(gatePair, data =>
        {
            var t = data.Data;
            channel.Writer.TryWrite(new Trade(t.CreateTime, t.Price, t.Quantity, t.Side == GateIo.Net.Enums.OrderSide.Sell));
        }, ct);

        try
        {
            if (!subResult.Success)
                yield break;

            await foreach (var trade in channel.Reader.ReadAllAsync(ct))
                yield return trade;
        }
        finally
        {
            socketClient.Dispose();
        }
    }

    public async Task<Candle[]> GetKlinesAsync(string pair, string interval, DateTime start, DateTime end, CancellationToken ct = default)
    {
        // Gate /api/v4/spot/candlesticks 按 from 升序返回, 单次最多 1000 条; 翻页通过推进 from
        var gp = pair.Replace("USDT", "_USDT");
        var ki = MapInterval(interval);
        var stepMs = IntervalMs(interval);
        var all = new List<Candle>();
        var seen = new HashSet<DateTime>();
        var cursor = start;
        while (cursor < end && !ct.IsCancellationRequested)
        {
            var r = await _client.SpotApi.ExchangeData.GetKlinesAsync(gp, ki, cursor, end, limit: 1000, ct: ct);
            if (!r.Success) throw new InvalidOperationException($"Gate K 线获取失败: {r.Error}");
            var batch = r.Data.OrderBy(k => k.OpenTime).ToArray();
            if (batch.Length == 0) break;
            var lastTime = cursor;
            foreach (var k in batch)
            {
                if (k.OpenTime < start || k.OpenTime > end) continue;
                if (seen.Add(k.OpenTime))
                    all.Add(new Candle(k.OpenTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.BaseVolume));
                if (k.OpenTime > lastTime) lastTime = k.OpenTime;
            }
            if (lastTime <= cursor) break;
            cursor = lastTime.AddMilliseconds(stepMs);
        }
        return all.OrderBy(c => c.Timestamp).ToArray();
    }

    private static long IntervalMs(string interval) => interval switch
    {
        "1m" => 60_000, "5m" => 300_000, "15m" => 900_000, "30m" => 1_800_000,
        "1h" => 3_600_000, "4h" => 14_400_000, "1d" => 86_400_000,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };

    public async Task<OrderBook> GetOrderBookAsync(string pair, int limit, CancellationToken ct = default)
    {
        var gp = pair.Replace("USDT", "_USDT");
        var r = await _client.SpotApi.ExchangeData.GetOrderBookAsync(gp, limit: limit, ct: ct);
        if (!r.Success) return new OrderBook(new decimal[0, 0], new decimal[0, 0], DateTime.UtcNow);
        return new OrderBook(ToDepth(r.Data.Bids), ToDepth(r.Data.Asks), DateTime.UtcNow);
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetTickersAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Select(t => new TickerPrice(t.Symbol, t.LastPrice, t.ChangePercentage24h, t.BaseVolume, t.HighPrice, t.LowPrice)).ToArray();
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var r = await _client.SpotApi.Account.GetBalancesAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Where(b => b.Available + b.Locked > 0)
            .ToDictionary(b => b.Asset, b => b.Available + b.Locked);
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default)
    {
        var b = await GetAssetBalancesAsync(ct);
        return b.Where(x => x.Key != "USDT").Select(x => new ExchangePosition($"{x.Key}USDT", x.Value, 0, 0, 0)).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var r = await _client.SpotApi.Trading.GetOpenOrdersAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.SelectMany(s => s.Orders).Select(MapOrder).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var bals = await GetAssetBalancesAsync(ct);
        var all = new List<ExchangeOrderDto>();
        foreach (var asset in bals.Keys.Where(a => a != "USDT").Select(a => $"{a}_USDT"))
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.SpotApi.Trading.GetOrdersAsync(false, asset, ct: ct);
            if (r.Success) all.AddRange(r.Data.Select(MapOrder));
        }
        return all.OrderByDescending(o => o.PlacedAt).ToArray();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var gp = request.Pair.Replace("USDT", "_USDT");
        var side = request.Side == OrderSide.Buy ? GateIo.Net.Enums.OrderSide.Buy : GateIo.Net.Enums.OrderSide.Sell;
        var type = request.Type == OrderType.Limit ? NewOrderType.Limit : NewOrderType.Market;
        var r = await _client.SpotApi.Trading.PlaceOrderAsync(gp, side, type,
            quantity: request.Quantity, price: request.Price, text: request.ClientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "下单失败");
        return new OrderResult(true, r.Data.Id.ToString(), r.Data.QuantityFilled, r.Data.AveragePrice ?? 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var gp = pair.Replace("USDT", "_USDT");
        var r = await _client.SpotApi.Trading.CancelOrderAsync(gp, orderId: long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "撤单失败");
        return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var gp = pair.Replace("USDT", "_USDT");
        var r = await _client.SpotApi.Trading.GetOrderAsync(gp, orderId: long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "查询订单失败");
        return new OrderResult(true, exchangeOrderId, r.Data.QuantityFilled, r.Data.AveragePrice ?? 0, 0, null);
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var gp = pair.Replace("USDT", "_USDT");
        var r = await _client.SpotApi.Trading.GetOrderAsync(gp, clientOrderId: clientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "按订单号查询失败");
        return new OrderResult(true, r.Data.Id.ToString(), r.Data.QuantityFilled, r.Data.AveragePrice ?? 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var balances = await GetAssetBalancesAsync(ct);
        var pairs = balances.Keys
            .Where(a => a != "USDT" && a != "USDC")
            .Select(a => $"{a}_USDT")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var openOrders = await GetOpenOrdersAsync(ct);
        foreach (var o in openOrders)
            pairs.Add(o.Pair);

        if (pairs.Count == 0) return [];

        var results = new List<OrderResult>();
        foreach (var pair in pairs)
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.SpotApi.Trading.GetOrdersAsync(false, pair, startTime: since, ct: ct);
            if (r.Success)
                results.AddRange(r.Data.Where(o => o.CreateTime >= since)
                    .Select(o => new OrderResult(o.Status == GateIo.Net.Enums.OrderStatus.Closed,
                        o.Id.ToString(), o.QuantityFilled, o.AveragePrice ?? 0, 0, null)));
        }
        return results.ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var ping = await _client.SpotApi.ExchangeData.GetTickersAsync(ct: ct);
            if (!ping.Success) return new ConnectionTestResult(false, null, ping.Error?.Message ?? "连接失败");
        }
        catch (Exception ex) { return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}"); }

        if (!_hasCredentials) return new ConnectionTestResult(true, null, "连接成功（未认证）");

        var bals = await _client.SpotApi.Account.GetBalancesAsync(ct: ct);
        return bals.Success
            ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "连接成功, API Key 有效")
            : new ConnectionTestResult(true, new() { ["spotTrade"] = false }, $"连接成功, 但 API Key 权限不足: {bals.Error}");
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetSymbolsAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Where(s => s.TradeStatus == SymbolStatus.Tradable)
            .Select(s => new PairRule(s.Name, s.PricePrecision, s.QuantityPrecision, 0, s.MinBaseQuantity, 0, 0)).ToArray();
    }

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHours, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };

    private static decimal[,] ToDepth(IEnumerable<GateIoOrderBookEntry>? entries)
    {
        if (entries is null) return new decimal[0, 0];
        var list = entries.ToArray();
        var r = new decimal[list.Length, 2];
        for (var i = 0; i < list.Length; i++) { r[i, 0] = list[i].Price; r[i, 1] = list[i].Quantity; }
        return r;
    }

    private static ExchangeOrderDto MapOrder(GateIoOrder o) => new(o.Symbol,
        o.Side == GateIo.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
        o.Type == GateIo.Net.Enums.OrderType.Limit ? "Limit" : "Market",
        o.Status switch { GateIo.Net.Enums.OrderStatus.Open => "New", GateIo.Net.Enums.OrderStatus.Closed => "Filled", GateIo.Net.Enums.OrderStatus.Canceled => "Cancelled", _ => o.Status.ToString() },
        o.Price ?? 0, o.Quantity, o.QuantityFilled, o.Id.ToString(), o.CreateTime);
}
