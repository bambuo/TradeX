using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class StrategyRepository(TradeXDbContext context) : IStrategyRepository
{
    public async Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<Strategy>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default)
        => await context.Strategies.Where(s => s.TraderId == traderId).OrderByDescending(s => s.UpdatedAtUtc).ToListAsync(ct);

    public async Task<List<Strategy>> GetAllActiveAsync(CancellationToken ct = default)
        => await context.Strategies
            .Where(s => s.Status == Core.Enums.StrategyStatus.Active)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ToListAsync(ct);

    public async Task<List<Strategy>> GetActiveByExchangeAndSymbolAsync(Guid exchangeId, string symbolId, CancellationToken ct = default)
    {
        var strategies = await context.Strategies
            .Where(s => s.ExchangeId == exchangeId && s.Status == Core.Enums.StrategyStatus.Active)
            .ToListAsync(ct);

        return strategies.Where(s => s.SymbolIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(symbolId)).ToList();
    }

    public async Task<bool> ExistsActiveAsync(Guid traderId, Guid exchangeId, string symbolId, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = context.Strategies
            .Where(s => s.TraderId == traderId && s.ExchangeId == exchangeId && s.Status == Core.Enums.StrategyStatus.Active);

        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);

        var strategies = await query.ToListAsync(ct);
        return strategies.Any(s =>
        {
            var symbols = s.SymbolIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return symbols.Contains(symbolId);
        });
    }

    public async Task AddAsync(Strategy strategy, CancellationToken ct = default)
    {
        await context.Strategies.AddAsync(strategy, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Strategy strategy, CancellationToken ct = default)
    {
        context.Strategies.Update(strategy);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Strategy strategy, CancellationToken ct = default)
    {
        context.Strategies.Remove(strategy);
        await context.SaveChangesAsync(ct);
    }
}
