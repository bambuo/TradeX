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

        services.AddScoped<DailyLossHandler>();
        services.AddScoped<DrawdownHandler>();
        services.AddScoped<ConsecutiveLossHandler>();
        services.AddScoped<CircuitBreakerHandler>();
        services.AddScoped<CooldownCheck>();
        services.AddScoped<PositionLimitHandler>();
        services.AddScoped<SlippageHandler>();
        services.AddScoped<ExchangeHealthHandler>();

        services.Configure<RiskSettings>(settings => { });

        services.AddSingleton<MarketDataCache>();
        services.AddHostedService<TradingEngine>();

        return services;
    }
}
