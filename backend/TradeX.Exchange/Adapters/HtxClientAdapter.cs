using System.Runtime.CompilerServices;
using HTX.Net;
using HTX.Net.Clients;
using HTX.Net.Enums;
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

    public async Task<Candle[]> GetKlinesAsync(string pair, string interval, DateTime start, DateTime end, CancellationToken ct = default)
    {
        // HTX /market/history/kline 不支持 from/to, 只接受 size(最大 2000), 返回最近 N 根降序;
        // 按 interval 估算需求条数, 取上限 2000, 客户端再 clip 到 [start, end].
        // 若 [start, end] 已超出 HTX 能返回的最早时间, 上层会触发 "未获取到回测 K 线" 失败.
        var stepMs = IntervalMs(interval);
        var needed = (int)Math.Ceiling((end - start).TotalMilliseconds / stepMs) + 10;
        var size = Math.Min(Math.Max(needed, 100), 2000);
        var r = await _client.SpotApi.ExchangeData.GetKlinesAsync(pair, MapInterval(interval), size, ct: ct);
        if (!r.Success) throw new InvalidOperationException($"HTX K 线获取失败: {r.Error}");
        return r.Data.Where(k => k.OpenTime >= start && k.OpenTime <= end)
            .Select(k => new Candle(k.OpenTime, k.OpenPrice.GetValueOrDefault(), k.HighPrice.GetValueOrDefault(), k.LowPrice.GetValueOrDefault(), k.ClosePrice.GetValueOrDefault(), k.Volume.GetValueOrDefault()))
            .OrderBy(c => c.Timestamp).ToArray();
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
        => new(true, null, "HTX SDK 就绪");

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default) => [];

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHours, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };
}
