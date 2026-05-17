using System.Runtime.CompilerServices;
using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Spot;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using ExchangeType = TradeX.Core.Enums.ExchangeType;
using OrderSide = TradeX.Core.Enums.OrderSide;
using OrderType = TradeX.Core.Enums.OrderType;

namespace TradeX.Exchange.Adapters;

public class BinanceClientAdapter : IExchangeClient
{
    private readonly BinanceRestClient _client;
    private readonly bool _hasCredentials;

    public ExchangeType Type => ExchangeType.Binance;

    public BinanceClientAdapter(string apiKey, string secretKey, bool isTestnet)
    {
        _hasCredentials = !string.IsNullOrWhiteSpace(apiKey);
        _client = new BinanceRestClient(o =>
        {
            if (_hasCredentials) o.ApiCredentials = new BinanceCredentials(apiKey, secretKey);
            if (isTestnet) o.Environment = BinanceEnvironment.Testnet;
        });
    }

    public async IAsyncEnumerable<Candle> SubscribeKlinesAsync(string pair, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastTime = 0L;
        while (!ct.IsCancellationRequested)
        {
            var candles = await GetKlinesAsync(pair, interval, lastTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastTime).UtcDateTime : DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, ct);
            foreach (var c in candles) { var ms = new DateTimeOffset(c.Timestamp).ToUnixTimeMilliseconds(); if (ms > lastTime) { lastTime = ms; yield return c; } }
            await Task.Delay(1000, ct);
        }
    }

    public async Task<Candle[]> GetKlinesAsync(string pair, string interval, DateTime start, DateTime end, CancellationToken ct = default)
    {
        var ki = MapInterval(interval);
        var r = await _client.SpotApi.ExchangeData.GetKlinesAsync(pair, ki, start, end, ct: ct);
        if (!r.Success) throw new InvalidOperationException($"Binance K 线获取失败: {r.Error}");
        return r.Data.Select(k => new Candle(k.OpenTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.Volume)).OrderBy(c => c.Timestamp).ToArray();
    }

    public async Task<OrderBook> GetOrderBookAsync(string pair, int limit, CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetOrderBookAsync(pair, limit, ct: ct);
        if (!r.Success) return new OrderBook(new decimal[0, 0], new decimal[0, 0], DateTime.UtcNow);
        return new OrderBook(ToDepth(r.Data.Bids), ToDepth(r.Data.Asks), DateTime.UtcNow);
    }

    public async Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetTickersAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Select(t => new TickerPrice(t.Symbol, t.LastPrice, 0, 0, 0, 0)).ToArray();
    }

    public async Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.Account.GetBalancesAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Where(b => b.Total > 0).ToDictionary(b => b.Asset, b => b.Total);
    }

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default)
    {
        var b = await GetAssetBalancesAsync(ct);
        return b.Where(x => x.Key != "USDT").Select(x => new ExchangePosition($"{x.Key}USDT", x.Value, 0, 0, 0)).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.Trading.GetOpenOrdersAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Select(MapOrder).ToArray();
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default)
    {
        var bals = await GetAssetBalancesAsync(ct);
        var all = new List<ExchangeOrderDto>();
        foreach (var pair in bals.Keys.Where(a => a != "USDT").Select(a => $"{a}USDT"))
        {
            var r = await _client.SpotApi.Trading.GetOrdersAsync(pair, ct: ct);
            if (r.Success) all.AddRange(r.Data.Select(MapOrder));
        }
        return all.OrderByDescending(o => o.PlacedAt).ToArray();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var side = request.Side == OrderSide.Buy ? Binance.Net.Enums.OrderSide.Buy : Binance.Net.Enums.OrderSide.Sell;
        var type = request.Type == OrderType.Limit ? SpotOrderType.Limit : SpotOrderType.Market;
        var r = await _client.SpotApi.Trading.PlaceOrderAsync(request.Pair, side, type,
            quantity: request.Quantity, price: request.Price, newClientOrderId: request.ClientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "下单失败");
        return new OrderResult(true, r.Data.Id.ToString(), r.Data.QuantityFilled, r.Data.AverageFillPrice ?? 0, 0, null);
    }

    public async Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        var r = await _client.SpotApi.Trading.CancelOrderAsync(pair, orderId: long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "撤单失败");
        return new OrderResult(true, exchangeOrderId, 0, 0, 0, null);
    }

    public async Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
    {
        var r = await _client.SpotApi.Trading.GetOrderAsync(pair, orderId: long.Parse(exchangeOrderId), ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "查询订单失败");
        return new OrderResult(true, exchangeOrderId, r.Data.QuantityFilled, r.Data.AverageFillPrice ?? 0, 0, null);
    }

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
    {
        var r = await _client.SpotApi.Trading.GetOrderAsync(pair, origClientOrderId: clientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "按订单号查询失败");
        return new OrderResult(true, r.Data.Id.ToString(), r.Data.QuantityFilled, r.Data.AverageFillPrice ?? 0, 0, null);
    }

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default)
    {
        var results = new List<OrderResult>();
        var infoR = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync(ct: ct);
        if (!infoR.Success) return [];
        foreach (var sym in infoR.Data.Symbols.Where(s => s.Status == SymbolStatus.Trading && s.IsSpotTradingAllowed))
        {
            var r = await _client.SpotApi.Trading.GetOrdersAsync(sym.Name, startTime: since, ct: ct);
            if (r.Success) results.AddRange(r.Data.Where(o => o.CreateTime >= since)
                .Select(o => new OrderResult(o.Status == Binance.Net.Enums.OrderStatus.Filled, o.Id.ToString(), o.QuantityFilled, o.AverageFillPrice ?? 0, 0, null)));
        }
        return results.ToArray();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var ping = await _client.SpotApi.ExchangeData.PingAsync(ct: ct);
            if (!ping.Success) return new ConnectionTestResult(false, null, "Ping 失败");
        }
        catch { return new ConnectionTestResult(false, null, "连接失败"); }
        if (!_hasCredentials) return new ConnectionTestResult(true, null, "Ping 成功");
        var r = await _client.SpotApi.Account.GetBalancesAsync(ct: ct);
        if (!r.Success) return new ConnectionTestResult(true, new() { ["spotTrade"] = false }, "API Key 无权限");
        return new ConnectionTestResult(true, new() { ["spotTrade"] = true }, "连接成功");
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Symbols.Where(s => s.Status == SymbolStatus.Trading && s.IsSpotTradingAllowed)
            .Select(s => new PairRule(s.Name, 8, 4, 0, 0, 0, 0)).ToArray();
    }

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHour, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };

    private static decimal[,] ToDepth(BinanceOrderBookEntry[] entries)
    {
        var r = new decimal[entries.Length, 2];
        for (var i = 0; i < entries.Length; i++) { r[i, 0] = entries[i].Price; r[i, 1] = entries[i].Quantity; }
        return r;
    }

    private static ExchangeOrderDto MapOrder(BinanceOrder o) => new(o.Symbol,
        o.Side == Binance.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
        o.Type == SpotOrderType.Limit ? "Limit" : "Market",
        o.Status switch { Binance.Net.Enums.OrderStatus.New => "New", Binance.Net.Enums.OrderStatus.PartiallyFilled => "PartiallyFilled", Binance.Net.Enums.OrderStatus.Filled => "Filled", Binance.Net.Enums.OrderStatus.Canceled => "Cancelled", Binance.Net.Enums.OrderStatus.Expired => "Expired", _ => o.Status.ToString() },
        o.Price, o.Quantity, o.QuantityFilled, o.Id.ToString(), o.CreateTime);
}
