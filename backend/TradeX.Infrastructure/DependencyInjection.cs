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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TradeXDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITraderRepository, TraderRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<IStrategyRepository, StrategyRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IBacktestTaskRepository, BacktestTaskRepository>();
        services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
        services.AddScoped<INotificationChannelRepository, NotificationChannelRepository>();

        services.AddSingleton<IEncryptionService, EncryptionService>();

        services.AddSingleton<IIoTDbService>(sp =>
        {
            var http = new HttpClient { BaseAddress = new Uri("http://localhost:8181"), Timeout = TimeSpan.FromSeconds(5) };
            return new IoTDbService(http, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IoTDbService>>());
        });

        services.AddSingleton<CasbinEnforcer>();

        return services;
    }

    public static IServiceCollection AddEncryption(this IServiceCollection services, string key)
    {
        services.Configure<EncryptionSettings>(settings => settings.Key = key);
        return services;
    }
}
