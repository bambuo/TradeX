using System.Runtime.CompilerServices;
using GateIo.Net;
using GateIo.Net.Clients;
using GateIo.Net.Enums;
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

    public Task<OrderBook> GetOrderBookAsync(string pair, int limit, CancellationToken ct = default)
        => Task.FromResult(new OrderBook(new decimal[0, 0], new decimal[0, 0], DateTime.UtcNow));

    public Task<TickerPrice[]> GetTickerPricesAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<TickerPrice>());

    public Task<Dictionary<string, decimal>> GetAssetBalancesAsync(CancellationToken ct = default)
        => Task.FromResult(new Dictionary<string, decimal>());

    public async Task<ExchangePosition[]> GetPositionsAsync(CancellationToken ct = default) => [];

    public async Task<ExchangeOrderDto[]> GetOpenOrdersAsync(CancellationToken ct = default) => [];

    public async Task<ExchangeOrderDto[]> GetOrderHistoryAsync(CancellationToken ct = default) => [];

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        => new(false, null, 0, 0, 0, "not_supported");

    public async Task<OrderResult> CancelOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
        => new(false, null, 0, 0, 0, "not_supported");

    public async Task<OrderResult> GetOrderAsync(string pair, string exchangeOrderId, CancellationToken ct = default)
        => new(false, null, 0, 0, 0, "not_supported");

    public async Task<OrderResult> GetOrderByClientOrderIdAsync(string pair, string clientOrderId, CancellationToken ct = default)
        => new(false, null, 0, 0, 0, "not_supported");

    public async Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct = default) => [];

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
        => new(true, null, "Gate SDK 就绪");

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default) => [];

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHours, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };
}
