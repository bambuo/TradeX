using Microsoft.EntityFrameworkCore;

namespace TradeX.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(TradeXDbContext db)
    {
        await db.Database.MigrateAsync();
    }
}
