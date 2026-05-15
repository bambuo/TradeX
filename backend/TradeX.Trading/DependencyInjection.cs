using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;

namespace TradeX.Trading;

public static class DependencyInjection
{
    public static IServiceCollection AddTrading(this IServiceCollection services)
    {
        services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
        services.AddScoped<IConditionTreeEvaluator, ConditionTreeEvaluator>();
        services.AddScoped<IPortfolioRiskManager, PortfolioRiskManager>();
        services.AddScoped<ITradeExecutor, TradeExecutor>();
        services.AddScoped<IOrderReconciler, OrderReconciler>();
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddScoped<BacktestEngine>();
        services.AddSingleton<IBacktestTaskQueue, BacktestTaskQueue>();
        services.AddSingleton<TaskAnalysisStore>();

        // Backtest 并发调度
        services.AddSingleton<IResourceProvider, SystemResourceProvider>();
        services.AddSingleton<ResourceMonitor>();
        services.AddHostedService<ResourceMonitor>(sp => sp.GetRequiredService<ResourceMonitor>());
        services.AddHostedService<BacktestScheduler>();

        services.AddScoped<DailyLossHandler>();
        services.AddScoped<DrawdownHandler>();
        services.AddScoped<ConsecutiveLossHandler>();
        services.AddScoped<CircuitBreakerHandler>();
        services.AddScoped<CooldownCheck>();
        services.AddScoped<PositionLimitHandler>();
        services.AddScoped<MaxOrderNotionalHandler>();
        services.AddScoped<SlippageHandler>();
        services.AddScoped<ExchangeHealthHandler>();

        services.Configure<RiskSettings>(settings => { });

        services.AddSingleton<MarketDataCache>();
        services.AddHostedService<TradingEngine>();

        return services;
    }
}
