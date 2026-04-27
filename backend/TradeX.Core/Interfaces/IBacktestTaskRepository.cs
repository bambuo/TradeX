using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IBacktestTaskRepository
{
    Task<BacktestTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<BacktestTask>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default);
    Task AddAsync(BacktestTask task, CancellationToken ct = default);
    Task UpdateAsync(BacktestTask task, CancellationToken ct = default);
    Task AddResultAsync(BacktestResult result, CancellationToken ct = default);
    Task<BacktestResult?> GetResultByTaskIdAsync(Guid taskId, CancellationToken ct = default);

    Task AddCandleAnalysesAsync(Guid taskId, IReadOnlyList<BacktestCandleAnalysis> analysis, CancellationToken ct = default);
    Task<BacktestCandleAnalysis[]> GetCandleAnalysesPageAsync(Guid taskId, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetCandleAnalysesCountAsync(Guid taskId, CancellationToken ct = default);
    Task<BacktestCandleAnalysis[]> GetCandleAnalysesAllAsync(Guid taskId, CancellationToken ct = default);
}
