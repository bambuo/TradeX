using System.Runtime.CompilerServices;
using OKX.Net;
using OKX.Net.Clients;
using OKX.Net.Enums;
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
        => new(true, null, "OKX SDK 就绪");

    public async Task<PairRule[]> GetPairRulesAsync(CancellationToken ct = default) => [];

    private static KlineInterval MapInterval(string interval) => interval switch
    {
        "1m" => KlineInterval.OneMinute, "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes, "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour, "4h" => KlineInterval.FourHours, "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"不支持的周期: {interval}")
    };
}
