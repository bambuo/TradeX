using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class ExchangeAccountRepository(TradeXDbContext db) : IExchangeAccountRepository
{
    public async Task<ExchangeAccount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.ExchangeAccounts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<List<ExchangeAccount>> GetAllEnabledAsync(CancellationToken ct = default) =>
        await db.ExchangeAccounts.Where(x => x.Status == ExchangeAccountStatus.Enabled).ToListAsync(ct);

    public async Task<List<ExchangeAccount>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await db.ExchangeAccounts
            .Where(ea => ea.TraderId == null || db.Traders.Any(t => t.Id == ea.TraderId && t.UserId == userId))
            .ToListAsync(ct);

    public async Task<bool> IsNameUniqueAsync(string name, CancellationToken ct = default) =>
        !await db.ExchangeAccounts.AnyAsync(x => x.Name == name, ct);

    public async Task AddAsync(ExchangeAccount account, CancellationToken ct = default)
    {
        db.ExchangeAccounts.Add(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ExchangeAccount account, CancellationToken ct = default)
    {
        account.UpdatedAt = DateTime.UtcNow;
        db.ExchangeAccounts.Update(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ExchangeAccount account, CancellationToken ct = default)
    {
        db.ExchangeAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
    }
}
