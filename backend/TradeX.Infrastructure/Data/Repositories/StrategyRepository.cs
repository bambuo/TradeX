using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class StrategyRepository(TradeXDbContext context) : IStrategyRepository
{
    public async Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<Strategy>> GetAllAsync(CancellationToken ct = default)
        => await context.Strategies.OrderByDescending(s => s.UpdatedAtUtc).ToListAsync(ct);

    public async Task AddAsync(Strategy strategy, CancellationToken ct = default)
    {
        await context.Strategies.AddAsync(strategy, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Strategy strategy, CancellationToken ct = default)
    {
        strategy.UpdatedAtUtc = DateTime.UtcNow;
        context.Strategies.Update(strategy);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Strategy strategy, CancellationToken ct = default)
    {
        context.Strategies.Remove(strategy);
        await context.SaveChangesAsync(ct);
    }
}
