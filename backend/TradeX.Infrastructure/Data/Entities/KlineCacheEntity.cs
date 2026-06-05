using TradeX.Core.Interfaces;

namespace TradeX.Infrastructure.Data.Entities;

public class KlineCacheEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ExchangeId { get; init; }
    public string Pair { get; init; } = null!;
    public string Timeframe { get; init; } = null!;
    public DateTime Timestamp { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }

    public static KlineCacheEntity FromCandle(Guid exchangeId, string pair, string timeframe, Candle c) => new()
    {
        ExchangeId = exchangeId,
        Pair = pair,
        Timeframe = timeframe,
        Timestamp = c.Timestamp,
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume
    };
}
