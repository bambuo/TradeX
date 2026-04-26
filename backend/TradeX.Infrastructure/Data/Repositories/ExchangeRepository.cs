using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class ExchangeRepository(TradeXDbContext db) : IExchangeRepository
{
    public async Task<Exchange?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Exchanges.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<List<Exchange>> GetAllEnabledAsync(CancellationToken ct = default) =>
        await db.Exchanges.Where(x => x.Status == ExchangeStatus.Enabled).ToListAsync(ct);

    public async Task<List<Exchange>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await db.Exchanges
            .Where(exchange => exchange.TraderId == null || db.Traders.Any(t => t.Id == exchange.TraderId && t.UserId == userId))
            .ToListAsync(ct);

    public async Task<bool> IsNameUniqueAsync(string name, CancellationToken ct = default) =>
        !await db.Exchanges.AnyAsync(x => x.Name == name, ct);

    public async Task AddAsync(Exchange exchange, CancellationToken ct = default)
    {
        db.Exchanges.Add(exchange);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Exchange exchange, CancellationToken ct = default)
    {
        exchange.UpdatedAt = DateTime.UtcNow;
        db.Exchanges.Update(exchange);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Exchange exchange, CancellationToken ct = default)
    {
        db.Exchanges.Remove(exchange);
        await db.SaveChangesAsync(ct);
    }
}
