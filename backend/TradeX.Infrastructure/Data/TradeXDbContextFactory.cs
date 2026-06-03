using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradeX.Infrastructure.Data;

/// <summary>
/// 设计期工厂，供 <c>dotnet ef migrations add ...</c> 使用。
/// 通过环境变量 TRADEX_DESIGN_CONNECTION 指定连接串，默认为本地 PostgreSQL。
/// </summary>
public class TradeXDbContextFactory : IDesignTimeDbContextFactory<TradeXDbContext>
{
    public TradeXDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRADEX_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=tradex;Username=tradex;Password=tradex;";

        var options = new DbContextOptionsBuilder<TradeXDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TradeXDbContext(options);
    }
}
