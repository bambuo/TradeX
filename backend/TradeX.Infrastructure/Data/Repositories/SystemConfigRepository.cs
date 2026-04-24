using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class SystemConfigRepository(TradeXDbContext context) : ISystemConfigRepository
{
    public async Task<List<SystemConfig>> GetAllAsync(CancellationToken ct = default)
        => await context.SystemConfigs.OrderBy(x => x.Key).ToListAsync(ct);

    public async Task<SystemConfig?> GetByKeyAsync(string key, CancellationToken ct = default)
        => await context.SystemConfigs.FirstOrDefaultAsync(x => x.Key == key, ct);

    public async Task UpsertAsync(string key, string value, CancellationToken ct = default)
    {
        var existing = await context.SystemConfigs.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            context.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
        }
        await context.SaveChangesAsync(ct);
    }
}
