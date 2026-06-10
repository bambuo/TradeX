using TradeX.Core.Interfaces;

namespace TradeX.Core.Interfaces;

public interface IKlineCacheRepository
{
    Task<Kline[]> GetKlinesAsync(Guid exchangeId, string pair, string timeframe, DateTime startAt, DateTime endAt, CancellationToken ct = default);
    Task SaveKlinesAsync(Guid exchangeId, string pair, string timeframe, Kline[] candles, CancellationToken ct = default);
}
