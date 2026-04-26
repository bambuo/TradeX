using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class StrategyDeploymentRepository(TradeXDbContext context) : IStrategyDeploymentRepository
{
    public async Task<StrategyDeployment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.StrategyDeployments.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<StrategyDeployment>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default)
        => await context.StrategyDeployments
            .Where(s => s.TraderId == traderId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<StrategyDeployment>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await context.StrategyDeployments
            .Where(s => context.Traders.Any(t => t.Id == s.TraderId && t.UserId == userId))
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<StrategyDeployment>> GetAllActiveAsync(CancellationToken ct = default)
        => await context.StrategyDeployments
            .Where(s => s.Status == Core.Enums.StrategyStatus.Active)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<StrategyDeployment>> GetActiveByExchangeAndSymbolAsync(Guid exchangeId, string symbolId, CancellationToken ct = default)
    {
        var deployments = await context.StrategyDeployments
            .Where(s => s.ExchangeId == exchangeId && s.Status == Core.Enums.StrategyStatus.Active)
            .ToListAsync(ct);

        return deployments.Where(s => ParseSymbolIds(s.SymbolIds).Contains(symbolId)).ToList();
    }

    public async Task<List<StrategyDeployment>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default)
        => await context.StrategyDeployments
            .Where(s => s.StrategyId == strategyId)
            .ToListAsync(ct);

    public async Task<bool> ExistsActiveAsync(Guid traderId, Guid exchangeId, string symbolId, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = context.StrategyDeployments
            .Where(s => s.TraderId == traderId && s.ExchangeId == exchangeId && s.Status == Core.Enums.StrategyStatus.Active);

        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);

        var deployments = await query.ToListAsync(ct);
        return deployments.Any(s => ParseSymbolIds(s.SymbolIds).Contains(symbolId));
    }

    private static string[] ParseSymbolIds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return [];
        try { return JsonSerializer.Deserialize<string[]>(raw) ?? []; }
        catch { return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); }
    }

    public async Task AddAsync(StrategyDeployment deployment, CancellationToken ct = default)
    {
        await context.StrategyDeployments.AddAsync(deployment, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(StrategyDeployment deployment, CancellationToken ct = default)
    {
        deployment.UpdatedAt = DateTime.UtcNow;
        context.StrategyDeployments.Update(deployment);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(StrategyDeployment deployment, CancellationToken ct = default)
    {
        var tasks = await context.BacktestTasks
            .Where(t => t.DeploymentId == deployment.Id)
            .ToListAsync(ct);
        if (tasks.Count > 0)
        {
            var taskIds = tasks.Select(t => t.Id).ToList();
            var results = await context.BacktestResults
                .Where(r => taskIds.Contains(r.TaskId))
                .ToListAsync(ct);
            context.BacktestResults.RemoveRange(results);
            context.BacktestTasks.RemoveRange(tasks);
        }

        context.StrategyDeployments.Remove(deployment);
        await context.SaveChangesAsync(ct);
    }
}
