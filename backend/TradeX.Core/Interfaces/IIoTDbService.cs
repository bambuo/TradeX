using TradeX.Core.Interfaces;

namespace TradeX.Core.Interfaces;

public interface IIoTDbService
{
    Task WriteKlinesAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candles, CancellationToken ct = default);
    Task<Candle[]> GetKlinesAsync(string exchange, string symbol, string interval, DateTime start, DateTime end, CancellationToken ct = default);
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}
