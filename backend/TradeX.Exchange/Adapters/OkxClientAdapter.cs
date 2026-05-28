using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OKX.Net;
using OKX.Net.Clients;
using OKX.Net.Enums;
using OKX.Net.Objects.Market;
using OKX.Net.Objects.Account;
using OKX.Net.Objects.Trade;
using OKX.Net.Objects.Public;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using ExchangeType = TradeX.Core.Enums.ExchangeType;
using OrderSide = TradeX.Core.Enums.OrderSide;
using OrderType = TradeX.Core.Enums.OrderType;

namespace TradeX.Exchange.Adapters;

public class OkxClientAdapter : IExchangeClient
{
    private readonly OKXRestClient _client;
    private readonly bool _hasCredentials;

    public ExchangeType Type => ExchangeType.OKX;

    public OkxClientAdapter(string apiKey, string secretKey, string? passphrase)
    {
        _hasCredentials = !string.IsNullOrWhiteSpace(apiKey);
        _client = new OKXRestClient(o => { if (_hasCredentials) o.ApiCredentials = new OKXCredentials(apiKey, secretKey, passphrase ?? ""); });
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
        var op = pair.Replace("USDT", "-USDT");
        var socketClient = new OKXSocketClient();
        var channel = Channel.CreateBounded<Candle>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.UnifiedApi.ExchangeData.SubscribeToKlineUpdatesAsync(op, MapInterval(interval), data =>
        {
            var k = data.Data;
            channel.Writer.TryWrite(new Candle(k.Time, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.Volume));
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
        var socketClient = new OKXSocketClient();
        var channel = Channel.CreateBounded<Trade>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.UnifiedApi.ExchangeData.SubscribeToTradeUpdatesAsync(pair, data =>
        {
            var t = data.Data;
            channel.Writer.TryWrite(new Trade(t.Time, t.Price, t.Quantity, t.Side == OKX.Net.Enums.OrderSide.Sell));
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
        // OKX /market/candles 仅含最近 1440 根, 历史回测必须走 /market/history-candles;
        // 单次最多 100 条, 按 after(endTime) 降序返回, 翻页通过回退 endTime
        var op = pair.Replace("USDT", "-USDT");
        var ki = MapInterval(interval);
        var stepMs = IntervalMs(interval);
        var all = new List<Candle>();
        var seen = new HashSet<DateTime>();
        var cursor = end;
        while (cursor > start && !ct.IsCancellationRequested)
        {
            var r = await _client.UnifiedApi.ExchangeData.GetKlineHistoryAsync(op, ki, start, cursor, 100, ct);
            if (!r.Success) throw new InvalidOperationException($"OKX K 线获取失败: {r.Error}");
            var batch = r.Data.OrderByDescending(k => k.Time).ToArray();
            if (batch.Length == 0) break;
            var earliest = cursor;
            foreach (var k in batch)
            {
                if (k.Time < start || k.Time > end) continue;
                if (seen.Add(k.Time))
                    all.Add(new Candle(k.Time, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.Volume));
                if (k.Time < earliest) earliest = k.Time;
            }
            if (earliest >= cursor) break;
            cursor = earliest.AddMilliseconds(-stepMs);
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
        var op = pair.Replace("USDT", "-USDT");
        var r = await _client.UnifiedApi.ExchangeData.GetOrderBookAsync(op, limit, ct);
        if (!r.Success) return new OrderBook(new decimal[0, 0], new decimal[0, 0], DateTime.UtcNow);
        return new OrderBook(ToDepth(r.Data.Bids), ToDepth(r.Data.Asks), DateTime.UtcNow);
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var r = await _client.UnifiedApi.ExchangeData.GetTickersAsync(InstrumentType.Spot, ct: ct);
        if (!r.Success) return [];
        return r.Data.Select(t => new TickerPrice(t.Symbol, t.LastPrice ?? 0, 0, t.Volume, t.HighPrice ?? 0, t.LowPrice ?? 0)).ToArray();
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var r = await _client.UnifiedApi.Account.GetAccountBalanceAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Details
            .Where(d => (d.Equity ?? 0) > 0)
            .ToDictionary(d => d.Asset, d => d.Equity ?? 0);
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default)
    {
        var b = await GetAssetBalancesAsync(ct);
        return b.Where(x => x.Key != "USDT").Select(x => new ExchangePosition($"{x.Key}USDT", x.Value, 0, 0, 0)).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var r = await _client.UnifiedApi.Trading.GetOrdersAsync(InstrumentType.Spot, ct: ct);
        if (!r.Success) return [];
        return r.Data.Select(MapOrder).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var bals = await GetAssetBalancesAsync(ct);
        var all = new List<ExchangeOrderDto>();
        foreach (var asset in bals.Keys.Where(a => a != "USDT").Select(a => $"{a}-USDT"))
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.UnifiedApi.Trading.GetOrderHistoryAsync(InstrumentType.Spot, symbol: asset, ct: ct);
            if (r.Success) all.AddRange(r.Data.Select(MapOrder));
        }
        return all.OrderByDescending(o => o.PlacedAt).ToArray();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var op = request.Pair.Replace("USDT", "-USDT");
        var side = request.Side == OrderSide.Buy ? OKX.Net.Enums.OrderSide.Buy : OKX.Net.Enums.OrderSide.Sell;
        var type = request.Type == OrderType.Limit ? OKX.Net.Enums.OrderType.Limit : OKX.Net.Enums.OrderType.Market;
        var r = await _client.UnifiedApi.Trading.PlaceOrderAsync(op, side, type,
            quantity: request.Quantity, price: request.Price, tradeMode: TradeMode.Cash,
            clientOrderId: request.ClientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "下单失败");
        return new OrderResult(true, r.Data.OrderId.ToString(), 0, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var op = pair.Replace("USDT", "-USDT");
        var r = await _client.UnifiedApi.Trading.CancelOrderAsync(op, orderId: long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "撤单失败");
        return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var op = pair.Replace("USDT", "-USDT");
        var r = await _client.UnifiedApi.Trading.GetOrderDetailsAsync(op, orderId: long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "查询订单失败");
        return new OrderResult(true, exchangeOrderId, r.Data.AccumulatedFillQuantity ?? 0, r.Data.AveragePrice ?? 0, 0, null);
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var op = pair.Replace("USDT", "-USDT");
        var r = await _client.UnifiedApi.Trading.GetOrderDetailsAsync(op, clientOrderId: clientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "按订单号查询失败");
        return new OrderResult(true, r.Data.OrderId.ToString(), r.Data.AccumulatedFillQuantity ?? 0, r.Data.AveragePrice ?? 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var balances = await GetAssetBalancesAsync(ct);
        var pairs = balances.Keys
            .Where(a => a != "USDT" && a != "USDC")
            .Select(a => $"{a}-USDT")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var openOrders = await GetOpenOrdersAsync(ct);
        foreach (var o in openOrders)
            pairs.Add(o.Pair);

        if (pairs.Count == 0) return [];

        var results = new List<OrderResult>();
        foreach (var pair in pairs)
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.UnifiedApi.Trading.GetOrderHistoryAsync(InstrumentType.Spot, symbol: pair, startTime: since, ct: ct);
            if (r.Success)
                results.AddRange(r.Data.Where(o => o.CreateTime >= since)
                    .Select(o => new OrderResult(o.OrderState == OKX.Net.Enums.OrderStatus.Filled,
                        o.OrderId.ToString(), o.AccumulatedFillQuantity ?? 0, o.AveragePrice ?? 0, 0, null)));
        }
        return results.ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var ping = await _client.UnifiedApi.ExchangeData.GetSymbolsAsync(InstrumentType.Spot, ct: ct);
            if (!ping.Success) return new ConnectionTestResult(false, null, ping.Error?.Message ?? "连接失败");
        }
        catch (Exception ex) { return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}"); }

        if (!_hasCredentials) return new ConnectionTestResult(true, null, "连接成功（未认证）");

        var bals = await _client.UnifiedApi.Account.GetAccountBalanceAsync(ct: ct);
        return bals.Success
            ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "连接成功, API Key 有效")
            : new ConnectionTestResult(true, new() { ["spotTrade"] = false }, $"连接成功, 但 API Key 权限不足: {bals.Error}");
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var r = await _client.UnifiedApi.ExchangeData.GetSymbolsAsync(InstrumentType.Spot, ct: ct);
        if (!r.Success) return [];
        return r.Data.Where(s => s.State == InstrumentState.Live)
            .Select(s => new PairRule(s.Symbol, 8, 4, 0, 0, s.TickSize ?? 0, s.LotSize ?? 0)).ToArray();
    }

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHours, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };

    private static decimal[,] ToDepth(IEnumerable<OKXOrderBookRow>? entries)
    {
        if (entries is null) return new decimal[0, 0];
        var list = entries.ToArray();
        var r = new decimal[list.Length, 2];
        for (var i = 0; i < list.Length; i++) { r[i, 0] = list[i].Price; r[i, 1] = list[i].Quantity; }
        return r;
    }

    private static ExchangeOrderDto MapOrder(OKXOrder o) => new(o.Symbol,
        o.OrderSide == OKX.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
        o.OrderType == OKX.Net.Enums.OrderType.Limit ? "Limit" : "Market",
        o.OrderState switch { OKX.Net.Enums.OrderStatus.Live => "New", OKX.Net.Enums.OrderStatus.PartiallyFilled => "PartiallyFilled", OKX.Net.Enums.OrderStatus.Filled => "Filled", OKX.Net.Enums.OrderStatus.Canceled => "Cancelled", _ => o.OrderState.ToString() },
        o.Price ?? 0, o.Quantity ?? 0, o.AccumulatedFillQuantity ?? 0, o.OrderId.ToString(), o.CreateTime);
}
