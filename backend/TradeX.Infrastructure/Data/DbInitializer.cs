using Microsoft.EntityFrameworkCore;

namespace TradeX.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(TradeXDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE BacktestTasks ADD COLUMN ExchangeId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'");
        }
        catch
        {
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE BacktestTasks ADD COLUMN Phase TEXT NULL");
        }
        catch
        {
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE BacktestResults ADD COLUMN AnalysisJson TEXT NULL");
        }
        catch
        {
        }
    }
}
