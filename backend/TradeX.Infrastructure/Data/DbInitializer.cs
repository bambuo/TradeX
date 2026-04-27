using Microsoft.EntityFrameworkCore;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(TradeXDbContext db)
    {
        await db.Database.MigrateAsync();

        var defaults = new Dictionary<string, string>
        {
            ["risk.volatility_grid_dedup_seconds"] = "60"
        };

        var changed = false;
        foreach (var (key, value) in defaults)
        {
            var exists = await db.SystemConfigs.AnyAsync(x => x.Key == key);
            if (exists)
                continue;

            db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }
}
