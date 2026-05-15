using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradeX.Infrastructure.Data;

/// <summary>
/// 设计期工厂，供 <c>dotnet ef migrations add ...</c> 使用。
/// 不要求 MySQL 实际运行，仅生成 SQL。如本机有 MySQL 可改为 ServerVersion.AutoDetect。
/// </summary>
public class TradeXDbContextFactory : IDesignTimeDbContextFactory<TradeXDbContext>
{
    public TradeXDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRADEX_DESIGN_CONNECTION")
            ?? "Server=localhost;Port=3306;Database=tradex;User Id=tradex;Password=tradex;SslMode=Preferred;CharSet=utf8mb4;";
        var serverVersion = new MySqlServerVersion(new Version(8, 4, 0));

        var options = new DbContextOptionsBuilder<TradeXDbContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;

        return new TradeXDbContext(options);
    }
}
