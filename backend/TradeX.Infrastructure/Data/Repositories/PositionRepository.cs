using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class PositionRepository(TradeXDbContext context) : IPositionRepository
{
    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Positions.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<List<Position>> GetOpenByTraderIdAsync(Guid traderId, CancellationToken ct = default)
        => await context.Positions
            .Where(p => p.TraderId == traderId && p.Status == Core.Enums.PositionStatus.Open)
            .OrderByDescending(p => p.OpenedAtUtc)
            .ToListAsync(ct);

    public async Task<List<Position>> GetAllOpenAsync(CancellationToken ct = default)
        => await context.Positions
            .Where(p => p.Status == Core.Enums.PositionStatus.Open)
            .OrderByDescending(p => p.OpenedAtUtc)
            .ToListAsync(ct);

    public async Task<List<Position>> GetOpenBySymbolAsync(Guid exchangeId, string symbolId, CancellationToken ct = default)
        => await context.Positions
            .Where(p => p.ExchangeId == exchangeId && p.SymbolId == symbolId && p.Status == Core.Enums.PositionStatus.Open)
            .ToListAsync(ct);

    public async Task<List<Position>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default)
        => await context.Positions
            .Where(p => p.StrategyId == strategyId)
            .OrderByDescending(p => p.OpenedAtUtc)
            .ToListAsync(ct);

    public async Task<List<Position>> GetClosedByTraderIdSinceAsync(Guid traderId, DateTime since, CancellationToken ct = default)
        => await context.Positions
            .Where(p => p.TraderId == traderId && p.Status == Core.Enums.PositionStatus.Closed && p.ClosedAtUtc >= since)
            .OrderByDescending(p => p.ClosedAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(Position position, CancellationToken ct = default)
    {
        await context.Positions.AddAsync(position, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Position position, CancellationToken ct = default)
    {
        context.Positions.Update(position);
        await context.SaveChangesAsync(ct);
    }
}
