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
        var gp = pair.Replace("USDT", "_USDT");
        var r = await _client.SpotApi.ExchangeData.GetKlinesAsync(gp, MapInterval(interval), start, end, ct: ct);
        if (!r.Success) throw new InvalidOperationException($"Gate K 线获取失败: {r.Error}");
        return r.Data.Select(k => new Candle(k.OpenTime, k.OpenPrice, k.HighPrice, k.LowPrice, k.ClosePrice, k.BaseVolume)).OrderBy(c => c.Timestamp).ToArray();
    }

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
