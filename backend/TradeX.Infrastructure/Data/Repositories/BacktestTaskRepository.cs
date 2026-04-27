using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data.Entities;

namespace TradeX.Infrastructure.Data.Repositories;

public class BacktestTaskRepository(TradeXDbContext context) : IBacktestTaskRepository
{
    public async Task<BacktestTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.BacktestTasks.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<List<BacktestTask>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default)
        => await context.BacktestTasks
            .Where(t => t.StrategyId == strategyId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(BacktestTask task, CancellationToken ct = default)
    {
        await context.BacktestTasks.AddAsync(task, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(BacktestTask task, CancellationToken ct = default)
    {
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

    public async Task AddCandleAnalysesAsync(Guid taskId, IReadOnlyList<BacktestCandleAnalysis> analysis, CancellationToken ct = default)
    {
        var entities = analysis.Select(a => BacktestCandleAnalysisEntity.FromDomain(taskId, a)).ToArray();
        await context.BacktestCandleAnalyses.AddRangeAsync(entities, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task<BacktestCandleAnalysis[]> GetCandleAnalysesPageAsync(Guid taskId, int page, int pageSize, string? actionFilter = null, CancellationToken ct = default)
    {
        var query = context.BacktestCandleAnalyses.Where(e => e.TaskId == taskId);
        if (!string.IsNullOrWhiteSpace(actionFilter) && actionFilter != "all")
            query = query.Where(e => e.Action == actionFilter);
        var entities = await query
            .OrderBy(e => e.Index)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
        return entities.Select(e => e.ToDomain()).ToArray();
    }

    public async Task<int> GetCandleAnalysesCountAsync(Guid taskId, string? actionFilter = null, CancellationToken ct = default)
    {
        var query = context.BacktestCandleAnalyses.Where(e => e.TaskId == taskId);
        if (!string.IsNullOrWhiteSpace(actionFilter) && actionFilter != "all")
            query = query.Where(e => e.Action == actionFilter);
        return await query.CountAsync(ct);
    }

    public async Task<BacktestCandleAnalysis[]> GetCandleAnalysesAllAsync(Guid taskId, CancellationToken ct = default)
    {
        var entities = await context.BacktestCandleAnalyses
            .Where(e => e.TaskId == taskId)
            .OrderBy(e => e.Index)
            .ToArrayAsync(ct);
        return entities.Select(e => e.ToDomain()).ToArray();
    }
}
