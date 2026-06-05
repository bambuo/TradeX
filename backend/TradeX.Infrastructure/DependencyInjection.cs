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
        string connectionString)
    {
        services.AddSingleton<VersionInterceptor>();
        services.AddSingleton<DomainEventInterceptor>();
        services.AddSingleton<DomainEventDispatcher>();
        services.AddDbContext<TradeXDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null)
                    .CommandTimeout(120))
                .AddInterceptors(
                    sp.GetRequiredService<VersionInterceptor>(),
                    sp.GetRequiredService<DomainEventInterceptor>())
                // 全局默认 NoTracking
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

        services.AddSingleton<IEncryptionService, EncryptionService>();
        services.AddSingleton<CasbinEnforcer>();

        // 应用层服务的 Infrastructure 实现（接口定义在 Core）
        services.AddScoped<Core.Interfaces.IHealthCheckService, Services.HealthCheckService>();

        return services;
    }

    public static IServiceCollection AddEncryption(this IServiceCollection services, string key)
    {
        services.Configure<EncryptionSettings>(settings => settings.Key = key);
        return services;
    }
}
