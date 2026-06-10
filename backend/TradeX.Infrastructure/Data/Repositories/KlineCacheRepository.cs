using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Infrastructure.Data.Entities;

namespace TradeX.Infrastructure.Data.Repositories;

public class KlineCacheRepository(TradeXDbContext context) : IKlineCacheRepository
{
    public async Task<Kline[]> GetKlinesAsync(Guid exchangeId, string pair, string timeframe, DateTime startAt, DateTime endAt, CancellationToken ct = default)
    {
        var entities = await context.KlineCache
            .Where(e => e.ExchangeId == exchangeId
                && e.Pair == pair
                && e.Timeframe == timeframe
                && e.Timestamp >= startAt
                && e.Timestamp <= endAt)
            .OrderBy(e => e.Timestamp)
            .ToArrayAsync(ct);

        return entities.Select(e => new Kline(e.Timestamp, e.Open, e.High, e.Low, e.Close, e.Volume)).ToArray();
    }

    public async Task SaveKlinesAsync(Guid exchangeId, string pair, string timeframe, Kline[] candles, CancellationToken ct = default)
    {
        if (candles.Length == 0) return;

        var minTs = candles.Min(c => c.Timestamp);
        var maxTs = candles.Max(c => c.Timestamp);
        var existingTimestamps = await context.KlineCache
            .Where(e => e.ExchangeId == exchangeId
                && e.Pair == pair
                && e.Timeframe == timeframe
                && e.Timestamp >= minTs
                && e.Timestamp <= maxTs)
            .Select(e => e.Timestamp)
            .ToHashSetAsync(ct);

        var newEntities = candles
            .Where(c => !existingTimestamps.Contains(c.Timestamp))
            .Select(c => KlineCacheEntity.FromKline(exchangeId, pair, timeframe, c))
            .ToArray();

        if (newEntities.Length == 0) return;

        await context.KlineCache.AddRangeAsync(newEntities, ct);
        await context.SaveChangesAsync(ct);
    }
}
