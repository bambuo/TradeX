using Skender.Stock.Indicators;

namespace TradeX.Indicators;

public class IndicatorService : IIndicatorService
{
    public decimal CalculateRsi(IReadOnlyList<decimal> prices, int period = 14)
    {
        if (prices.Count < period + 1)
            return 50;

        var quotes = ToQuotes(prices);
        var results = quotes.GetRsi(period);
        var last = results.LastOrDefault();
        return last?.Rsi is not null ? Math.Round((decimal)last.Rsi.Value, 2) : 50;
    }

    public (decimal MacdLine, decimal SignalLine, decimal Histogram) CalculateMacd(
        IReadOnlyList<decimal> prices, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        if (prices.Count < slowPeriod + signalPeriod)
            return (0, 0, 0);

        var quotes = ToQuotes(prices);
        var results = quotes.GetMacd(fastPeriod, slowPeriod, signalPeriod);
        var last = results.LastOrDefault();
        if (last is null)
            return (0, 0, 0);

        return (
            (decimal)(last.Macd ?? 0),
            (decimal)(last.Signal ?? 0),
            (decimal)(last.Histogram ?? 0)
        );
    }

    public decimal CalculateSma(IReadOnlyList<decimal> prices, int period)
    {
        if (prices.Count < period)
            return 0;

        var quotes = ToQuotes(prices);
        var results = quotes.GetSma(period);
        return (decimal)(results.LastOrDefault()?.Sma ?? 0);
    }

    public decimal CalculateEma(IReadOnlyList<decimal> prices, int period)
    {
        if (prices.Count < period)
            return 0;

        var quotes = ToQuotes(prices);
        var results = quotes.GetEma(period);
        return (decimal)(results.LastOrDefault()?.Ema ?? 0);
    }

    public (decimal UpperBand, decimal MiddleBand, decimal LowerBand) CalculateBollingerBands(
        IReadOnlyList<decimal> prices, int period = 20, decimal stdDev = 2)
    {
        if (prices.Count < period)
            return (0, 0, 0);

        var quotes = ToQuotes(prices);
        var results = quotes.GetBollingerBands(period, (double)stdDev);
        var last = results.LastOrDefault();
        if (last is null)
            return (0, 0, 0);

        return (
            (decimal)(last.UpperBand ?? 0),
            (decimal)(last.Sma ?? 0),
            (decimal)(last.LowerBand ?? 0)
        );
    }

    public decimal CalculateVolumeSma(IReadOnlyList<long> volumes, int period = 20)
    {
        if (volumes.Count < period)
            return 0;

        return Math.Round((decimal)volumes.TakeLast(period).Average(), 0);
    }

    public decimal CalculateObv(IReadOnlyList<decimal> prices, IReadOnlyList<long> volumes)
    {
        if (prices.Count < 2)
            return 0;

        var quotes = prices.Select((p, i) => new Quote
        {
            Close = p,
            Volume = volumes[i]
        }).ToList();

        var results = quotes.GetObv();
        return Math.Round((decimal)(results.LastOrDefault()?.Obv ?? 0), 2);
    }

    public (decimal K, decimal D) CalculateStochRsi(
        IReadOnlyList<decimal> prices, int rsiPeriod = 14, int stochPeriod = 14,
        int kSmoothing = 3, int dSmoothing = 3)
    {
        if (prices.Count < rsiPeriod + stochPeriod + kSmoothing)
            return (50, 50);

        var quotes = ToQuotes(prices);
        var results = quotes.GetStochRsi(rsiPeriod, stochPeriod, kSmoothing, dSmoothing);
        var last = results.LastOrDefault();
        if (last is null)
            return (50, 50);

        return (
            Math.Round((decimal)(last.StochRsi ?? 50), 2),
            Math.Round((decimal)(last.Signal ?? 50), 2)
        );
    }

    private static List<Quote> ToQuotes(IReadOnlyList<decimal> prices)
    {
        return prices.Select((p, i) => new Quote
        {
            Date = DateTime.UtcNow.AddMinutes(-(prices.Count - 1 - i)),
            Close = p,
            High = p,
            Low = p,
            Open = p,
            Volume = 0
        }).ToList();
    }
}
