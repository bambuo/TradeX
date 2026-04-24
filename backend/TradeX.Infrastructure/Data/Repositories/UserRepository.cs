using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class UserRepository(TradeXDbContext db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Users.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        await db.Users.FirstOrDefaultAsync(x => x.Username == username && !x.IsDeleted, ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await db.Users.FirstOrDefaultAsync(x => x.Email == email && !x.IsDeleted, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<User>> GetAllAsync(CancellationToken ct = default) =>
        await db.Users.Where(x => !x.IsDeleted).ToListAsync(ct);
}
