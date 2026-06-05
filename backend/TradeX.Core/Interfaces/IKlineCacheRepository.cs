using TradeX.Core.Interfaces;

namespace TradeX.Core.Interfaces;

public interface IKlineCacheRepository
{
    Task<Candle[]> GetKlinesAsync(Guid exchangeId, string pair, string timeframe, DateTime startAt, DateTime endAt, CancellationToken ct = default);
    Task SaveKlinesAsync(Guid exchangeId, string pair, string timeframe, Candle[] candles, CancellationToken ct = default);
}
