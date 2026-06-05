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

    public async IAsyncEnumerable<Candle> SubscribeKlinesStreamAsync(string pair, string interval, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var socketClient = new BinanceSocketClient();
        var channel = System.Threading.Channels.Channel.CreateBounded<Candle>(new System.Threading.Channels.BoundedChannelOptions(100)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(pair, MapInterval(interval), data =>
        {
            var k = data.Data.Data;
            channel.Writer.TryWrite(new Candle(k.OpenTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.Volume));
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
        var socketClient = new BinanceSocketClient();
        var channel = System.Threading.Channels.Channel.CreateBounded<Trade>(new System.Threading.Channels.BoundedChannelOptions(100)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
        });

        var subResult = await socketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(pair, data =>
        {
            var t = data.Data;
            channel.Writer.TryWrite(new Trade(t.TradeTime, t.Price, t.Quantity, t.BuyerIsMaker));
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
        // Binance /api/v3/klines 按 startTime 升序返回, 单次最多 1000 条; 翻页通过推进 startTime
        var ki = MapInterval(interval);
        var stepMs = IntervalMs(interval);
        var all = new List<Candle>();
        var seen = new HashSet<DateTime>();
        var cursor = start;
        while (cursor < end && !ct.IsCancellationRequested)
        {
            var r = await _client.SpotApi.ExchangeData.GetKlinesAsync(pair, ki, cursor, end, limit: 1000, ct: ct);
            if (!r.Success) throw new InvalidOperationException($"Binance K 线获取失败: {r.Error}");
            var batch = r.Data.OrderBy(k => k.OpenTime).ToArray();
            if (batch.Length == 0) break;
            var lastTime = cursor;
            foreach (var k in batch)
            {
                if (k.OpenTime < start || k.OpenTime > end) continue;
                if (seen.Add(k.OpenTime))
                    all.Add(new Candle(k.OpenTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.Volume));
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

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(int limit = 100, CancellationToken ct = default)
    {
        var bals = await GetAssetBalancesAsync(ct);
        var all = new List<ExchangeOrderDto>();
        foreach (var pair in bals.Keys.Where(a => a != "USDT").Select(a => $"{a}USDT"))
        {
            var r = await _client.SpotApi.Trading.GetOrdersAsync(pair, limit: limit, ct: ct);
            if (r.Success) all.AddRange(r.Data.Select(MapOrder));
        }
        return [.. all.OrderByDescending(o => o.PlacedAt).Take(limit)];
    }

    public async Task<ExchangeOrderDto[]> GetOrderHistoryByPairAsync(string pair, int limit = 100, CancellationToken ct = default)
    {
        var r = await _client.SpotApi.Trading.GetOrdersAsync(pair, limit: limit, ct: ct);
        if (!r.Success) return [];
        return r.Data.Select(MapOrder).ToArray();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var side = request.Side == OrderSide.Buy ? Binance.Net.Enums.OrderSide.Buy : Binance.Net.Enums.OrderSide.Sell;
        var type = request.Type == OrderType.Limit ? SpotOrderType.Limit : SpotOrderType.Market;
        var r = await _client.SpotApi.Trading.PlaceOrderAsync(request.Pair, side, type,
            quantity: request.Quantity, price: request.Price, newClientOrderId: request.ClientOrderId, ct: ct);
        if (!r.Success) return new OrderResult(false, null, 0, 0, 0, r.Error?.Message ?? "下单失败");
        // 手续费来自成交明细（Trades），同一订单可能含多笔不同费币，汇总后取首笔费币
        var fee = r.Data.Trades?.Sum(t => t.Fee) ?? 0m;
        var feeAsset = r.Data.Trades?.FirstOrDefault()?.FeeAsset;
        return new OrderResult(true, r.Data.Id.ToString(), r.Data.QuantityFilled, r.Data.AverageFillPrice ?? 0,
            Math.Abs(fee), null, feeAsset);
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
        // 不遍历全部 400+ 交易对（会触发限流熔断），
        // 改为通过持仓资产 + 当前未结订单推导需要查询的交易对。
        // 覆盖不了"已清仓且不再持有"的交易对，但此方法当前无实际调用方，
        // 将来若需全量扫描，调用方可改传已知交易对列表。
        var balances = await GetAssetBalancesAsync(ct);
        var pairs = balances.Keys
            .Where(a => a != "USDT" && a != "USDC")
            .Select(a => $"{a}USDT")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 补充当前挂单涉及但已无持仓的交易对
        var openOrders = await GetOpenOrdersAsync(ct);
        foreach (var o in openOrders)
            pairs.Add(o.Pair);

        if (pairs.Count == 0) return [];

        var results = new List<OrderResult>();
        foreach (var pair in pairs)
        {
            if (ct.IsCancellationRequested) break;
            var r = await _client.SpotApi.Trading.GetOrdersAsync(pair, startTime: since, ct: ct);
            if (r.Success)
                results.AddRange(r.Data.Where(o => o.CreateTime >= since)
                    .Select(o => new OrderResult(o.Status == Binance.Net.Enums.OrderStatus.Filled,
                        o.Id.ToString(), o.QuantityFilled, o.AverageFillPrice ?? 0, 0, null)));
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

        // FR-02.5: 必须区分 (a) 现货交易权限是否开启 (b) 提现权限是否已关闭
        var perms = await _client.SpotApi.Account.GetAPIKeyPermissionsAsync(ct: ct);
        if (!perms.Success)
        {
            // 退化: GetAPIKeyPermissions 失败 (旧版 Key 可能不支持), 改用 GetBalances 推断只读权限
            var bals = await _client.SpotApi.Account.GetBalancesAsync(ct: ct);
            return bals.Success
                ? new ConnectionTestResult(true, new() { ["spotTrade"] = false, ["withdraw"] = false, ["readOnly"] = true },
                    "无法读取 API Key 权限位, 已退化为只读检测")
                : new ConnectionTestResult(true, new() { ["spotTrade"] = false }, $"API Key 无权限: {bals.Error}");
        }

        var p = perms.Data;
        var permMap = new Dictionary<string, bool>
        {
            ["spotTrade"] = p.EnableSpotAndMarginTrading,
            ["withdraw"] = p.EnableWithdrawals,
            ["reading"] = p.EnableReading,
            ["ipRestrict"] = p.IpRestrict
        };

        var warnings = new List<string>();
        if (!p.EnableSpotAndMarginTrading) warnings.Add("现货交易权限未开启");
        if (p.EnableWithdrawals) warnings.Add("⚠ 提现权限已开启, 强烈建议在 Binance 后台关闭");
        if (!p.IpRestrict) warnings.Add("未配置 IP 白名单, 建议在 Binance 后台限制访问 IP");

        var message = warnings.Count > 0 ? string.Join("; ", warnings) : "连接成功, 权限校验通过";
        return new ConnectionTestResult(true, permMap, message);
    }

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default)
    {
        var r = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync(ct: ct);
        if (!r.Success) return [];
        return r.Data.Symbols.Where(s => s.Status == SymbolStatus.Trading && s.IsSpotTradingAllowed)
            .Select(s =>
            {
                var tickSize = s.PriceFilter?.TickSize ?? 0m;
                var stepSize = s.LotSizeFilter?.StepSize ?? 0m;
                var minQty = s.LotSizeFilter?.MinQuantity ?? 0m;
                var minNotional = s.NotionalFilter?.MinNotional ?? s.MinNotionalFilter?.MinNotional ?? 0m;
                return new PairRule(s.Name,
                    PairRuleMath.PrecisionFromStep(tickSize), PairRuleMath.PrecisionFromStep(stepSize),
                    minNotional, minQty, tickSize, stepSize);
            }).ToArray();
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
