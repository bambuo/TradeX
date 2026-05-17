using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;
using TradeX.Infrastructure.Casbin;
using TradeX.Infrastructure.Data;
using TradeX.Infrastructure.Data.Repositories;
using TradeX.Infrastructure.Services;
using TradeX.Infrastructure.Settings;

namespace TradeX.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string mySqlVersion = "8.4.0")
    {
        var serverVersion = new MySqlServerVersion(new Version(mySqlVersion));
        services.AddSingleton<VersionInterceptor>();
        services.AddDbContext<TradeXDbContext>((sp, options) =>
            options
                .UseMySql(connectionString, serverVersion, mysql => mysql
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null))
                .AddInterceptors(sp.GetRequiredService<VersionInterceptor>())
                // 全局默认 NoTracking — 读路径热点不再 ChangeTracker 开销；
                // 需要修改的地方用 .AsTracking() 显式开启，或 ctx.Update/Attach
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .EnableSensitiveDataLogging(false));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITraderRepository, TraderRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IExchangeRepository, ExchangeRepository>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IStrategyBindingRepository, StrategyBindingRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IBacktestTaskRepository, BacktestTaskRepository>();
        services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
        services.AddScoped<INotificationChannelRepository, NotificationChannelRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();

        services.AddSingleton<IEncryptionService, EncryptionService>();
        services.AddSingleton<CasbinEnforcer>();

        return services;
    }

    public static IServiceCollection AddEncryption(this IServiceCollection services, string key)
    {
        services.Configure<EncryptionSettings>(settings => settings.Key = key);
        return services;
    }
}
