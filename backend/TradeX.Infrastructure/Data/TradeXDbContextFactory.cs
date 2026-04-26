using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradeX.Infrastructure.Data;

public class TradeXDbContextFactory : IDesignTimeDbContextFactory<TradeXDbContext>
{
    public TradeXDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TradeXDbContext>()
            .UseSqlite("Data Source=tradex.db")
            .Options;

        return new TradeXDbContext(options);
    }
}
