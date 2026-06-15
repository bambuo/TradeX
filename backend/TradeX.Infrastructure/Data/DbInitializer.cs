using Microsoft.EntityFrameworkCore;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(TradeXDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        // 记录系统默认配置
    }
}
