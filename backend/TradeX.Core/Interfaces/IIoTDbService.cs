using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IIoTDbService
{
    Task WriteKlinesAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candles, CancellationToken ct = default);
    Task<Candle[]> GetKlinesAsync(string exchange, string symbol, string interval, DateTime start, DateTime end, CancellationToken ct = default);
    Task<bool> HealthCheckAsync(CancellationToken ct = default);

    Task WriteBacktestAnalysisAsync(Guid taskId, IReadOnlyList<BacktestCandleAnalysis> analysis, CancellationToken ct = default);
    Task<BacktestCandleAnalysis[]> GetBacktestAnalysisPageAsync(Guid taskId, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetBacktestAnalysisCountAsync(Guid taskId, CancellationToken ct = default);
    Task<BacktestCandleAnalysis[]> GetBacktestAnalysisAllAsync(Guid taskId, CancellationToken ct = default);
    Task DeleteBacktestAnalysisAsync(Guid taskId, CancellationToken ct = default);
}
