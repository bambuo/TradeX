using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HTX.Net;
using HTX.Net.Clients;
using HTX.Net.Enums;
using HTX.Net.Objects.Models;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using ExchangeType = TradeX.Core.Enums.ExchangeType;
using OrderSide = TradeX.Core.Enums.OrderSide;
using OrderType = TradeX.Core.Enums.OrderType;

namespace TradeX.Exchange.Adapters;

public class HtxClientAdapter : IExchangeClient
{
    private readonly HTXRestClient _client;
    private readonly bool _hasCredentials;

    public ExchangeType Type => ExchangeType.HTX;

    public HtxClientAdapter(string apiKey, string secretKey)
    {
        _hasCredentials = !string.IsNullOrWhiteSpace(apiKey);
        _client = new HTXRestClient(o => { if (_hasCredentials) o.ApiCredentials = new HTXCredentials(apiKey, secretKey); });
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
        var socketClient = new HTXSocketClient();
        var channel = Channel.CreateBounded<Candle>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.SpotApi.SubscribeToKlineUpdatesAsync(pair, MapInterval(interval), data =>
        {
            var k = data.Data;
            channel.Writer.TryWrite(new Candle(k.OpenTime, k.OpenPrice.GetValueOrDefault(), k.HighPrice.GetValueOrDefault(), k.LowPrice.GetValueOrDefault(), k.ClosePrice.GetValueOrDefault(), k.Volume.GetValueOrDefault()));
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
        var socketClient = new HTXSocketClient();
        var channel = Channel.CreateBounded<Trade>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.SpotApi.SubscribeToTradeUpdatesAsync(pair, data =>
        {
            var t = data.Data;
            foreach (var detail in t.Details)
            {
                channel.Writer.TryWrite(new Trade(detail.Timestamp, detail.Price, detail.Quantity, detail.Side == HTX.Net.Enums.OrderSide.Sell));
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
        // HTX /market/history/kline 不支持 from/to, 只接受 size(最大 2000), 返回最近 N 根降序.
        // 若窗口需要的条数超过 2000, 直接拒绝, 否则会静默回退到"最近 2000 根"产生错误窗口数据.
        const int HtxMaxSize = 2000;
        var stepMs = IntervalMs(interval);
        var needed = (int)Math.Ceiling((end - start).TotalMilliseconds / stepMs) + 10;
        if (needed > HtxMaxSize)
            throw new InvalidOperationException(
                $"HTX 历史 K 线单次最多 {HtxMaxSize} 根, 当前窗口需要 {needed} 根 (Pair={pair}, Interval={interval}). 请缩短回测区间或选用更大周期.");

        var size = Math.Min(Math.Max(needed, 100), HtxMaxSize);
        var r = await _client.SpotApi.ExchangeData.GetKlinesAsync(pair, MapInterval(interval), size, ct: ct);
        if (!r.Success) throw new InvalidOperationException($"HTX K 线获取失败: {r.Error}");

        var candles = r.Data.Where(k => k.OpenTime >= start && k.OpenTime <= end)
            .Select(k => new Candle(k.OpenTime, k.OpenPrice.GetValueOrDefault(), k.HighPrice.GetValueOrDefault(), k.LowPrice.GetValueOrDefault(), k.ClosePrice.GetValueOrDefault(), k.Volume.GetValueOrDefault()))
            .OrderBy(c => c.Timestamp).ToArray();

        // HTX 返回最近 N 根, 若请求起点早于最早返回点, 实际窗口被截断, 显式抛错而非交给上层"零条"判定
        if (candles.Length > 0 && candles[0].Timestamp - start > TimeSpan.FromMilliseconds(stepMs * 2))
            throw new InvalidOperationException(
                $"HTX 数据起点 ({candles[0].Timestamp:O}) 晚于请求起点 ({start:O}), 历史数据不足. 请缩短回测区间.");

        return candles;
    }

    private static long IntervalMs(string interval) => interval switch
    {
        "1m" => 60_000, "5m" => 300_000, "15m" => 900_000, "30m" => 1_800_000,
        "1h" => 3_600_000, "4h" => 14_400_000, "1d" => 86_400_000,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };

    public async Task<OrderBook> GetOrderBookAsync(string pair, int limit, CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetOrderBookAsync(pair, 0, limit: limit, ct);
        if (!r.Success) return new OrderBook(new decimal[0, 0], new decimal[0, 0], DateTime.UtcNow);
        return new OrderBook(ToDepth(r.Data.Bids), ToDepth(r.Data.Asks), DateTime.UtcNow);
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetTickersAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Ticks.Select(t => new TickerPrice(t.Symbol, t.LastTradePrice, 0, 0, 0, 0)).ToArray();
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var accounts = await _client.SpotApi.Account.GetAccountsAsync(ct: ct);
        if (!accounts.Success || accounts.Data is null) return [];
        var spotAccount = accounts.Data.FirstOrDefault();
        if (spotAccount is null) return [];
        var r = await _client.SpotApi.Account.GetBalancesAsync(spotAccount.Id, ct: ct);
        if (!r.Success) return [];
        return r.Data.Where(b => b.Type == BalanceType.Trade && b.Balance > 0)
            .ToDictionary(b => b.Asset, b => b.Balance);
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
        return r.Data.Select(MapOpenOrder).ToArray();
    }

    private static ExchangeOrderDto MapOpenOrder(HTXOpenOrder o) => new(o.Symbol,
        o.Side == HTX.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
        o.Type == HTX.Net.Enums.OrderType.Limit ? "Limit" : "Market",
        o.Status switch { HTX.Net.Enums.OrderStatus.Submitted => "New", HTX.Net.Enums.OrderStatus.PartiallyFilled => "PartiallyFilled", HTX.Net.Enums.OrderStatus.Filled => "Filled", HTX.Net.Enums.OrderStatus.Canceled => "Cancelled", _ => o.Status.ToString() },
        o.Price, o.Quantity, o.QuantityFilled, o.Id.ToString(), o.CreateTime);

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        if (!_hasCredentials) return [];
        var bals = await GetAssetBalancesAsync(ct);
        var all = new List<ExchangeOrderDto>();
        foreach (var asset in bals.Keys.Where(a => a != "USDT").Select(a => $"{a}USDT"))
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.SpotApi.Trading.GetClosedOrdersAsync(asset, ct: ct);
            if (r.Success) all.AddRange(r.Data.Select(MapOrder));
        }
        return all.OrderByDescending(o => o.PlacedAt).ToArray();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var accounts = await _client.SpotApi.Account.GetAccountsAsync(ct: ct);
        if (!accounts.Success || accounts.Data is null)
            return new OrderResult(false, null, 0, 0, 0, "获取账户失败");
        var spotAccount = accounts.Data.FirstOrDefault();
        if (spotAccount is null)
            return new OrderResult(false, null, 0, 0, 0, "未找到现货账户");
        var side = request.Side == OrderSide.Buy ? HTX.Net.Enums.OrderSide.Buy : HTX.Net.Enums.OrderSide.Sell;
        var type = request.Type == OrderType.Limit ? HTX.Net.Enums.OrderType.Limit : HTX.Net.Enums.OrderType.Market;
        var r = await _client.SpotApi.Trading.PlaceOrderAsync(spotAccount.Id, request.Pair, side, type,
            quantity: request.Quantity, price: request.Price, clientOrderId: request.ClientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "下单失败");
        // HTX 下单仅返回 orderId，成交量/均价/手续费需回查订单补全
        var orderId = r.Data.ToString();
        var detail = await GetOrderAsync(request.Pair, orderId, ct);
        return detail.Success
            ? detail with { ExchangeOrderId = orderId }
            : new OrderResult(true, orderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var r = await _client.SpotApi.Trading.CancelOrderAsync(long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "撤单失败");
        return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var r = await _client.SpotApi.Trading.GetOrderAsync(long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "查询订单失败");
        var avg = r.Data.QuantityFilled > 0 ? r.Data.QuoteQuantityFilled / r.Data.QuantityFilled : 0m;
        return new OrderResult(true, exchangeOrderId, r.Data.QuantityFilled, avg, Math.Abs(r.Data.Fee), null);
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        if (!_hasCredentials) return new OrderResult(false, null, 0, 0, 0, "未配置 API Key");
        var r = await _client.SpotApi.Trading.GetOrderByClientOrderIdAsync(clientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "按订单号查询失败");
        var avg = r.Data.QuantityFilled > 0 ? r.Data.QuoteQuantityFilled / r.Data.QuantityFilled : 0m;
        return new OrderResult(true, r.Data.Id.ToString(), r.Data.QuantityFilled, avg, Math.Abs(r.Data.Fee), null);
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
            var r = await _client.SpotApi.Trading.GetClosedOrdersAsync(pair, startTime: since, ct: ct);
            if (r.Success)
                results.AddRange(r.Data.Where(o => o.CreateTime >= since)
                    .Select(o => new OrderResult(o.Status == HTX.Net.Enums.OrderStatus.Filled,
                        o.Id.ToString(), o.QuantityFilled, 0, o.Fee, null)));
        }
        return results.ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var ping = await _client.SpotApi.ExchangeData.GetSymbolsAsync(ct: ct);
            if (!ping.Success) return new ConnectionTestResult(false, null, ping.Error?.Message ?? "连接失败");
        }
        catch (Exception ex) { return new ConnectionTestResult(false, null, $"连接异常: {ex.Message}"); }

        if (!_hasCredentials) return new ConnectionTestResult(true, null, "连接成功（未认证）");

        var accounts = await _client.SpotApi.Account.GetAccountsAsync(ct: ct);
        return accounts.Success
            ? new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "连接成功, API Key 有效")
            : new ConnectionTestResult(true, new() { ["spotTrade"] = false }, $"连接成功, 但 API Key 权限不足: {accounts.Error}");
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetSymbolsAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Where(s => s.SymbolStatus == SymbolStatus.Online)
            .Select(s => new PairRule(s.Name, (int)s.PricePrecision, (int)s.QuantityPrecision, 0, 0, 0, 0)).ToArray();
    }

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHours, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };

    private static decimal[,] ToDepth(IEnumerable<HTXOrderBookEntry>? entries)
    {
        if (entries is null) return new decimal[0, 0];
        var list = entries.ToArray();
        var r = new decimal[list.Length, 2];
        for (var i = 0; i < list.Length; i++) { r[i, 0] = list[i].Price; r[i, 1] = list[i].Quantity; }
        return r;
    }

    private static ExchangeOrderDto MapOrder(HTXOrder o) => new(o.Symbol,
        o.Side == HTX.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
        o.Type == HTX.Net.Enums.OrderType.Limit ? "Limit" : "Market",
        o.Status switch { HTX.Net.Enums.OrderStatus.Submitted => "New", HTX.Net.Enums.OrderStatus.PartiallyFilled => "PartiallyFilled", HTX.Net.Enums.OrderStatus.Filled => "Filled", HTX.Net.Enums.OrderStatus.Canceled => "Cancelled", _ => o.Status.ToString() },
        o.Price, o.Quantity, o.QuantityFilled, o.Id.ToString(), o.CreateTime);
}
