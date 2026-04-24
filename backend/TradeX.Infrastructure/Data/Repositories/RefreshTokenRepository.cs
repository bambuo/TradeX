using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class RefreshTokenRepository(TradeXDbContext db) : IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        await db.RefreshTokens.FirstOrDefaultAsync(x => x.Token == token && x.RevokedAt == null, ct);

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens)
            t.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
