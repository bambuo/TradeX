using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using ExchangeType = TradeX.Core.Enums.ExchangeType;
using OrderSide = TradeX.Core.Enums.OrderSide;
using OrderType = TradeX.Core.Enums.OrderType;

namespace TradeX.Exchange.Adapters;

public class BybitClientAdapter : IExchangeClient
{
    private readonly BybitRestClient _client;
    private readonly bool _hasCredentials;

    public ExchangeType Type => ExchangeType.Bybit;

    public BybitClientAdapter(string apiKey, string secretKey, bool isTestnet)
    {
        _hasCredentials = !string.IsNullOrWhiteSpace(apiKey);
        _client = new BybitRestClient(o =>
        {
            if (_hasCredentials) o.ApiCredentials = new BybitCredentials(apiKey, secretKey);
            if (isTestnet) o.Environment = BybitEnvironment.Testnet;
        });
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
        var socketClient = new BybitSocketClient();
        var channel = Channel.CreateBounded<Candle>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.V5SpotApi.SubscribeToKlineUpdatesAsync(pair, MapInterval(interval), data =>
        {
            if (data.Data is null) return;
            foreach (var k in data.Data)
            {
                channel.Writer.TryWrite(new Candle(k.StartTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.Volume));
            }
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
        var socketClient = new BybitSocketClient();
        var channel = Channel.CreateBounded<Trade>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.V5SpotApi.SubscribeToTradeUpdatesAsync(pair, data =>
        {
            if (data.Data is null) return;
            foreach (var t in data.Data)
            {
                channel.Writer.TryWrite(new Trade(t.Timestamp, t.Price, t.Quantity, t.Side == Bybit.Net.Enums.OrderSide.Sell));
            }
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
        // Bybit V5 spot /v5/market/kline 按 endTime 降序返回, 单次最多 1000 条; 翻页通过回退 endTime
        var ki = MapInterval(interval);
        var stepMs = IntervalMs(interval);
        var all = new List<Candle>();
        var seen = new HashSet<DateTime>();
        var cursor = end;
        while (cursor > start && !ct.IsCancellationRequested)
        {
            var r = await _client.V5Api.ExchangeData.GetKlinesAsync(Category.Spot, pair, ki, start, cursor, 1000, ct);
            if (!r.Success) throw new InvalidOperationException($"Bybit K 线获取失败: {r.Error}");
            var batch = r.Data.List.OrderByDescending(k => k.StartTime).ToArray();
            if (batch.Length == 0) break;
            var earliest = cursor;
            foreach (var k in batch)
            {
                if (k.StartTime < start || k.StartTime > end) continue;
                if (seen.Add(k.StartTime))
                    all.Add(new Candle(k.StartTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.Volume));
                if (k.StartTime < earliest) earliest = k.StartTime;
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
        var r = await _client.V5Api.ExchangeData.GetOrderbookAsync(Category.Spot, pair, limit, ct);
        if (!r.Success) return new OrderBook(new decimal[0, 0], new decimal[0, 0], DateTime.UtcNow);
        return new OrderBook(ToDepth(r.Data.Bids), ToDepth(r.Data.Asks), DateTime.UtcNow);
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var r = await _client.V5Api.ExchangeData.GetSpotTickersAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.List.Select(t => new TickerPrice(t.Symbol, t.LastPrice, t.PriceChangePercentag24h, t.Volume24h, t.HighPrice24h, t.LowPrice24h)).ToArray();
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var r = await _client.V5Api.Account.GetBalancesAsync(AccountType.Spot, ct: ct);
        if (!r.Success) return [];
        return r.Data.List
            .SelectMany(b => b.Assets)
            .Where(a => (a.Free ?? 0) + (a.Locked ?? 0) > 0)
            .ToDictionary(a => a.Asset, a => (a.Free ?? 0) + (a.Locked ?? 0));
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default)
    {
        var b = await GetAssetBalancesAsync(ct);
        return b.Where(x => x.Key != "USDT").Select(x => new ExchangePosition($"{x.Key}USDT", x.Value, 0, 0, 0)).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var r = await _client.V5Api.Trading.GetOrdersAsync(Category.Spot, openOnly: 1, ct: ct);
        if (!r.Success) return [];
        return r.Data.List.Select(MapOrder).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var bals = await GetAssetBalancesAsync(ct);
        var all = new List<ExchangeOrderDto>();
        foreach (var asset in bals.Keys.Where(a => a != "USDT").Select(a => $"{a}USDT"))
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.V5Api.Trading.GetOrderHistoryAsync(Category.Spot, symbol: asset, ct: ct);
            if (r.Success) all.AddRange(r.Data.List.Select(MapOrder));
        }
        return all.OrderByDescending(o => o.PlacedAt).ToArray();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var side = request.Side == OrderSide.Buy ? Bybit.Net.Enums.OrderSide.Buy : Bybit.Net.Enums.OrderSide.Sell;
        var type = request.Type == OrderType.Limit ? NewOrderType.Limit : NewOrderType.Market;
        var r = await _client.V5Api.Trading.PlaceOrderAsync(Category.Spot, request.Pair, side, type,
            quantity: request.Quantity, price: request.Price, clientOrderId: request.ClientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "下单失败");
        // Bybit 下单仅返回 orderId，成交量/均价/手续费需回查订单补全
        var orderId = r.Data.OrderId;
        var detail = await GetOrderAsync(request.Pair, orderId, ct);
        return detail.Success
            ? detail with { ExchangeOrderId = orderId }
            : new OrderResult(true, orderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var r = await _client.V5Api.Trading.CancelOrderAsync(Category.Spot, pair, orderId: exchangeOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "撤单失败");
        return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var r = await _client.V5Api.Trading.GetOrdersAsync(Category.Spot, pair, orderId: exchangeOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "查询订单失败");
        var o = r.Data.List.FirstOrDefault();
        if (o is null) return new OrderResult(false, null, 0, 0, 0, "订单不存在");
        return new OrderResult(true, exchangeOrderId, o.QuantityFilled ?? 0, o.AveragePrice ?? 0, Math.Abs(o.ExecutedFee ?? 0), null, o.FeeAsset);
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var r = await _client.V5Api.Trading.GetOrdersAsync(Category.Spot, pair, clientOrderId: clientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "按订单号查询失败");
        var o = r.Data.List.FirstOrDefault();
        if (o is null) return new OrderResult(false, null, 0, 0, 0, "订单不存在");
        return new OrderResult(true, o.OrderId, o.QuantityFilled ?? 0, o.AveragePrice ?? 0, Math.Abs(o.ExecutedFee ?? 0), null, o.FeeAsset);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var balances = await GetAssetBalancesAsync(ct);
        var pairs = balances.Keys
            .Where(a => a != "USDT" && a != "USDC")
            .Select(a => $"{a}USDT")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var openOrders = await GetOpenOrdersAsync(ct);
        foreach (var o in openOrders)
            pairs.Add(o.Pair);

        if (pairs.Count == 0) return [];

        var results = new List<OrderResult>();
        foreach (var pair in pairs)
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.V5Api.Trading.GetOrderHistoryAsync(Category.Spot, symbol: pair, startTime: since, ct: ct);
            if (r.Success)
                results.AddRange(r.Data.List.Where(o => o.CreateTime >= since)
                    .Select(o => new OrderResult(o.Status == Bybit.Net.Enums.OrderStatus.Filled,
                        o.OrderId, o.QuantityFilled ?? 0, o.AveragePrice ?? 0, 0, null)));
        }
        return results.ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var ping = await _client.V5Api.ExchangeData.GetSpotTickersAsync(ct: ct);
            if (!ping.Success) return new ConnectionTestResult(false, null, ping.Error?.Message ?? "连接失败");
        }
        catch (Exception ex) { return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}"); }

        if (!_hasCredentials) return new ConnectionTestResult(true, null, "连接成功（未认证）");

        var keyInfo = await _client.V5Api.Account.GetApiKeyInfoAsync(ct: ct);
        return keyInfo.Success
            ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "连接成功, API Key 有效")
            : new ConnectionTestResult(true, new() { ["spotTrade"] = false }, $"连接成功, 但 API Key 权限不足: {keyInfo.Error}");
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var r = await _client.V5Api.ExchangeData.GetSpotSymbolsAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.List.Where(s => s.Status == SymbolStatus.Trading)
            .Select(s => new PairRule(s.Name, 8, 4, 0,
                s.LotSizeFilter?.MinOrderQuantity ?? 0,
                s.PriceFilter?.TickSize ?? 0,
                s.LotSizeFilter?.BasePrecision ?? 0)).ToArray();
    }

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHours, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };

    private static decimal[,] ToDepth(BybitOrderbookEntry[]? entries)
    {
        if (entries is null) return new decimal[0, 0];
        var r = new decimal[entries.Length, 2];
        for (var i = 0; i < entries.Length; i++) { r[i, 0] = entries[i].Price; r[i, 1] = entries[i].Quantity; }
        return r;
    }

    private static ExchangeOrderDto MapOrder(BybitOrder o) => new(o.Symbol,
        o.Side == Bybit.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
        o.OrderType == Bybit.Net.Enums.OrderType.Limit ? "Limit" : "Market",
        o.Status switch { Bybit.Net.Enums.OrderStatus.New => "New", Bybit.Net.Enums.OrderStatus.PartiallyFilled => "PartiallyFilled", Bybit.Net.Enums.OrderStatus.Filled => "Filled", Bybit.Net.Enums.OrderStatus.Cancelled => "Cancelled", _ => o.Status.ToString() },
        o.Price ?? 0, o.Quantity, o.QuantityFilled ?? 0, o.OrderId, o.CreateTime);
}
