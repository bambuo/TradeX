using Microsoft.EntityFrameworkCore;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class TraderRepository(TradeXDbContext db) : ITraderRepository
{
    public async Task<Trader?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Traders.FirstOrDefaultAsync(x => x.Id == id && x.Status != TraderStatus.Deleted, ct);

    public async Task<List<Trader>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await db.Traders.Where(x => x.UserId == userId && x.Status != TraderStatus.Deleted).ToListAsync(ct);

    public async Task<bool> IsNameUniqueAsync(Guid userId, string name, Guid? excludeId = null, CancellationToken ct = default) =>
        !await db.Traders.AnyAsync(x => x.UserId == userId && x.Name == name && x.Status != TraderStatus.Deleted && (excludeId == null || x.Id != excludeId), ct);

    public async Task AddAsync(Trader trader, CancellationToken ct = default)
    {
        db.Traders.Add(trader);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Trader trader, CancellationToken ct = default)
    {
        trader.UpdatedAt = DateTime.UtcNow;
        db.Traders.Update(trader);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Trader trader, CancellationToken ct = default)
    {
        trader.Status = TraderStatus.Deleted;
        trader.UpdatedAt = DateTime.UtcNow;
        db.Traders.Update(trader);
        await db.SaveChangesAsync(ct);
    }
}
