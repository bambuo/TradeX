using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;

namespace TradeX.Trading;

public static class DependencyInjection
{
    /// <summary>
    /// 两端共用的 Trading 服务：策略评估器、风控管线、TradeExecutor、回测服务等。
    /// 不包含任何 BackgroundService 启动；调用方需根据进程角色另行决定。
    /// API 进程仅需 <see cref="AddTradingShared"/>；Worker 进程在此之上再 <see cref="AddTradingWorker"/>。
    /// </summary>
    public static IServiceCollection AddTradingShared(this IServiceCollection services)
    {
        services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
        services.AddScoped<IConditionTreeEvaluator, ConditionTreeEvaluator>();
        services.AddScoped<IPortfolioRiskManager, PortfolioRiskManager>();
        services.AddScoped<ITradeExecutor, TradeExecutor>();
        services.AddScoped<IOrderReconciler, OrderReconciler>();
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddScoped<BacktestEngine>();

        // 注意：以下 Singleton 当前是进程内实现（Channel / 内存）。
        // 跨进程语义在阶段 3/4 通过 Redis 解决；此前 API 端入队不会被 Worker 端消费。
        services.AddSingleton<IBacktestTaskQueue, BacktestTaskQueue>();
        services.AddSingleton<TaskAnalysisStore>();

        // ResourceMonitor 作为 Singleton 注册，仅在 Worker 端通过 AddTradingWorker 加 HostedService 真正运行。
        // API 端能注入到 HealthController 但读到的是默认值 0（不会真正采样）。
        services.AddSingleton<IResourceProvider, SystemResourceProvider>();
        services.AddSingleton<ResourceMonitor>();

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

        return services;
    }

    /// <summary>
    /// Worker 进程独占：启动 TradingEngine 评估循环 / BacktestScheduler 回测调度 / ResourceMonitor 资源采样。
    /// 同时注册 Worker 内存缓存 <see cref="MarketDataCache"/>。
    /// </summary>
    public static IServiceCollection AddTradingWorker(this IServiceCollection services)
    {
        services.AddSingleton<MarketDataCache>();
        services.AddHostedService<ResourceMonitor>(sp => sp.GetRequiredService<ResourceMonitor>());
        services.AddHostedService<BacktestScheduler>();
        services.AddHostedService<TradingEngine>();
        return services;
    }
}
