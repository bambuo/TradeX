using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data.Entities;

namespace TradeX.Infrastructure.Data.Repositories;

public class BacktestTaskRepository(TradeXDbContext context) : IBacktestTaskRepository
{
    public async Task<bool> ClaimTaskAsync(Guid id, BacktestTaskStatus fromStatus, BacktestPhase phase, CancellationToken ct = default)
    {
        var rows = await context.BacktestTasks
            .Where(t => t.Id == id && t.Status == fromStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.Status, BacktestTaskStatus.Running)
                    .SetProperty(t => t.Phase, phase),
                ct);
        return rows > 0;
    }

    public async Task<bool> ExecuteInTransactionAsync(Func<IBacktestTaskRepository, CancellationToken, Task<bool>> action, CancellationToken ct = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            try
            {
                var commit = await action(this, ct);
                if (commit)
                {
                    await transaction.CommitAsync(ct);
                    return true;
                }

                await transaction.RollbackAsync(ct);
                return false;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<BacktestTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.BacktestTasks.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<List<BacktestTask>> GetAllAsync(CancellationToken ct = default)
        => await context.BacktestTasks
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<BacktestTask>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default)
        => await context.BacktestTasks
            .Where(t => t.StrategyId == strategyId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<BacktestTask>> GetByStatusAsync(BacktestTaskStatus status, CancellationToken ct = default)
        => await context.BacktestTasks
            .Where(t => t.Status == status)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(BacktestTask task, CancellationToken ct = default)
    {
        await context.BacktestTasks.AddAsync(task, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(BacktestTask task, CancellationToken ct = default)
    {
        // 全局 NoTracking 下每次查询返回新实例；同一 scope 内对同一任务多次 Update
        // （阶段推进 Queued→Running→FetchingData→… 及写结果/收尾）会因同主键被重复 Attach
        // 抛 "instance ... is already being tracked"。Attach 前先剥离已追踪的陈旧实例。
        var stale = context.ChangeTracker.Entries<BacktestTask>()
            .FirstOrDefault(e => e.Entity.Id == task.Id && !ReferenceEquals(e.Entity, task));
        if (stale is not null)
            stale.State = EntityState.Detached;

        context.BacktestTasks.Update(task);
        await context.SaveChangesAsync(ct);
    }

    public async Task AddResultAsync(BacktestResult result, CancellationToken ct = default)
    {
        await context.BacktestResults.AddAsync(result, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task<BacktestResult?> GetResultByTaskIdAsync(Guid taskId, CancellationToken ct = default)
        => await context.BacktestResults.FirstOrDefaultAsync(r => r.TaskId == taskId, ct);

    public async Task AddKlineAnalysesAsync(Guid taskId, IReadOnlyList<BacktestKlineAnalysis> analysis, CancellationToken ct = default)
    {
        var entities = analysis.Select(a => BacktestKlineAnalysisEntity.FromDomain(taskId, a)).ToArray();
        await context.BacktestKlineAnalyses.AddRangeAsync(entities, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task<BacktestKlineAnalysis[]> GetKlineAnalysesPageAsync(Guid taskId, int page, int pageSize, string? actionFilter = null, CancellationToken ct = default)
    {
        var query = context.BacktestKlineAnalyses.Where(e => e.TaskId == taskId);
        if (!string.IsNullOrWhiteSpace(actionFilter) && actionFilter != "all")
            query = query.Where(e => e.Action == actionFilter);
        var entities = await query
            .OrderBy(e => e.Index)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
        return entities.Select(e => e.ToDomain()).ToArray();
    }

    public async Task DeleteKlineAnalysesByTaskIdAsync(Guid taskId, CancellationToken ct = default)
    {
        var stale = await context.BacktestKlineAnalyses
            .Where(e => e.TaskId == taskId)
            .ToArrayAsync(ct);
        if (stale.Length == 0) return;
        context.BacktestKlineAnalyses.RemoveRange(stale);
        await context.SaveChangesAsync(ct);
    }

    public async Task<int> GetKlineAnalysesCountAsync(Guid taskId, string? actionFilter = null, CancellationToken ct = default)
    {
        var query = context.BacktestKlineAnalyses.Where(e => e.TaskId == taskId);
        if (!string.IsNullOrWhiteSpace(actionFilter) && actionFilter != "all")
            query = query.Where(e => e.Action == actionFilter);
        return await query.CountAsync(ct);
    }

    public async Task<BacktestKlineAnalysis[]> GetKlineAnalysesAllAsync(Guid taskId, CancellationToken ct = default)
    {
        var entities = await context.BacktestKlineAnalyses
            .Where(e => e.TaskId == taskId)
            .OrderBy(e => e.Index)
            .ToArrayAsync(ct);
        return entities.Select(e => e.ToDomain()).ToArray();
    }
}
