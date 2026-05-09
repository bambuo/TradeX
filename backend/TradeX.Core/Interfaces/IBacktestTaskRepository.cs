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

    Task AddKlineAnalysesAsync(Guid taskId, IReadOnlyList<BacktestKlineAnalysis> analysis, CancellationToken ct = default);
    Task<BacktestKlineAnalysis[]> GetKlineAnalysesPageAsync(Guid taskId, int page, int pageSize, string? actionFilter = null, CancellationToken ct = default);
    Task<int> GetKlineAnalysesCountAsync(Guid taskId, string? actionFilter = null, CancellationToken ct = default);
    Task<BacktestKlineAnalysis[]> GetKlineAnalysesAllAsync(Guid taskId, CancellationToken ct = default);
}
