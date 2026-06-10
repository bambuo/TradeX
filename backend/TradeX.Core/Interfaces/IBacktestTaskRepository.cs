using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IBacktestTaskRepository
{
    Task<BacktestTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<BacktestTask>> GetAllAsync(CancellationToken ct = default);
    Task<List<BacktestTask>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default);
    Task<List<BacktestTask>> GetByStatusAsync(BacktestTaskStatus status, CancellationToken ct = default);
    Task AddAsync(BacktestTask task, CancellationToken ct = default);
    Task UpdateAsync(BacktestTask task, CancellationToken ct = default);
    Task AddResultAsync(BacktestResult result, CancellationToken ct = default);
    Task<BacktestResult?> GetResultByTaskIdAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// 乐观锁：原子性地将任务从 <paramref name="fromStatus"/> 推进到 Running 状态。
    /// 用于回测 Worker 竞争获取任务所有权，防止多实例同时处理同一任务。
    /// 底层 SQL: <c>UPDATE backtest_tasks SET status='Running', phase=@phase WHERE id=@id AND status=@fromStatus</c>
    /// </summary>
    /// <returns>true 表示成功抢到任务；false 表示状态不符（任务已被其他 Worker 处理或已取消）。</returns>
    Task<bool> ClaimTaskAsync(Guid id, BacktestTaskStatus fromStatus, BacktestPhase phase, CancellationToken ct = default);

    /// <summary>
    /// 在数据库事务中执行多个写操作，保证原子性。
    /// action 返回 true 表示提交事务；false 表示回滚。
    /// </summary>
    Task<bool> ExecuteInTransactionAsync(Func<IBacktestTaskRepository, CancellationToken, Task<bool>> action, CancellationToken ct = default);

    Task AddKlineAnalysesAsync(Guid taskId, IReadOnlyList<BacktestKlineAnalysis> analysis, CancellationToken ct = default);
    Task<BacktestKlineAnalysis[]> GetKlineAnalysesPageAsync(Guid taskId, int page, int pageSize, string? actionFilter = null, CancellationToken ct = default);
    Task<int> GetKlineAnalysesCountAsync(Guid taskId, string? actionFilter = null, CancellationToken ct = default);
    Task<BacktestKlineAnalysis[]> GetKlineAnalysesAllAsync(Guid taskId, CancellationToken ct = default);
    Task DeleteKlineAnalysesByTaskIdAsync(Guid taskId, CancellationToken ct = default);
}
