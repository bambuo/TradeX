using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class BacktestTaskRepository(TradeXDbContext context) : IBacktestTaskRepository
{
    public async Task<BacktestTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.BacktestTasks.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<List<BacktestTask>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default)
        => await context.BacktestTasks
            .Where(t => t.StrategyId == strategyId)
            .OrderByDescending(t => t.CreatedAtUtc)
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
}
