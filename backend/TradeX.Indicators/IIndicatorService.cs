namespace TradeX.Indicators;

public interface IIndicatorService
{
    decimal CalculateRsi(IReadOnlyList<decimal> prices, int period = 14);
    (decimal MacdLine, decimal SignalLine, decimal Histogram) CalculateMacd(IReadOnlyList<decimal> prices, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9);
    decimal CalculateSma(IReadOnlyList<decimal> prices, int period);
    decimal CalculateEma(IReadOnlyList<decimal> prices, int period);
    (decimal UpperBand, decimal MiddleBand, decimal LowerBand) CalculateBollingerBands(IReadOnlyList<decimal> prices, int period = 20, decimal stdDev = 2);
    decimal CalculateVolumeSma(IReadOnlyList<long> volumes, int period = 20);
    decimal CalculateObv(IReadOnlyList<decimal> prices, IReadOnlyList<long> volumes);
    (decimal K, decimal D) CalculateStochRsi(IReadOnlyList<decimal> prices, int rsiPeriod = 14, int stochPeriod = 14, int kSmoothing = 3, int dSmoothing = 3);
}
