using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class StrategyBindingRepository(TradeXDbContext context, ILogger<StrategyBindingRepository> logger) : IStrategyBindingRepository
{
    public async Task<StrategyBinding?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.StrategyBindings.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<StrategyBinding>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default)
        => await context.StrategyBindings
            .Where(s => s.TraderId == traderId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<StrategyBinding>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await context.StrategyBindings
            .Where(s => context.Traders.Any(t => t.Id == s.TraderId && t.UserId == userId))
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<StrategyBinding>> GetAllActiveAsync(CancellationToken ct = default)
        => await context.StrategyBindings
            .Where(s => s.Status == Core.Enums.BindingStatus.Active)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<StrategyBinding>> GetActiveByExchangeAndSymbolAsync(Guid exchangeId, string symbolId, CancellationToken ct = default)
    {
        var bindings = await context.StrategyBindings
            .Where(s => s.ExchangeId == exchangeId && s.Status == Core.Enums.BindingStatus.Active)
            .ToListAsync(ct);

        return bindings.Where(s => ParsePairs(s.Pairs).Contains(symbolId)).ToList();
    }

    public async Task<List<StrategyBinding>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default)
        => await context.StrategyBindings
            .Where(s => s.StrategyId == strategyId)
            .ToListAsync(ct);

    public async Task<bool> ExistsActiveAsync(Guid traderId, Guid exchangeId, string symbolId, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = context.StrategyBindings
            .Where(s => s.TraderId == traderId && s.ExchangeId == exchangeId && s.Status == Core.Enums.BindingStatus.Active);

        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);

        var bindings = await query.ToListAsync(ct);
        return bindings.Any(s => ParsePairs(s.Pairs).Contains(symbolId));
    }

    private string[] ParsePairs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return [];
        try { return JsonSerializer.Deserialize<string[]>(raw) ?? []; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SymbolIds JSON 解析失败，回退到逗号分割");
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public async Task AddAsync(StrategyBinding deployment, CancellationToken ct = default)
    {
        await context.StrategyBindings.AddAsync(deployment, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(StrategyBinding deployment, CancellationToken ct = default)
    {
        deployment.UpdatedAt = DateTime.UtcNow;
        context.StrategyBindings.Update(deployment);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(StrategyBinding deployment, CancellationToken ct = default)
    {
        context.StrategyBindings.Remove(deployment);
        await context.SaveChangesAsync(ct);
    }
}
