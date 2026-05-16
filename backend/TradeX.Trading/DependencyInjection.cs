using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;
using TradeX.Trading.Backtest;
using TradeX.Trading.Commands;

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
        services.AddHostedService<OrderReconcilerService>();
        return services;
    }

    /// <summary>
    /// 注册 Worker 端的命令处理器集合 + Redis 订阅者。调用方需确保 <c>IConnectionMultiplexer</c> 已注册。
    /// 调用顺序：先注册自定义 handler（实现 <see cref="IWorkerCommandHandler"/>），再调用本方法。
    /// </summary>
    public static IServiceCollection AddTradingWorkerCommandBus(this IServiceCollection services)
    {
        services.AddSingleton<IWorkerCommandHandler, ReconcileNowHandler>();
        services.AddHostedService<WorkerCommandSubscriber>();
        return services;
    }

    /// <summary>注册命令发布者；Redis 配置存在用 RedisWorkerCommandPublisher，否则降级 NullWorkerCommandPublisher。</summary>
    public static IServiceCollection AddTradingCommandPublisher(this IServiceCollection services, bool redisAvailable)
    {
        if (redisAvailable)
            services.AddSingleton<IWorkerCommandPublisher, RedisWorkerCommandPublisher>();
        else
            services.AddSingleton<IWorkerCommandPublisher, NullWorkerCommandPublisher>();
        return services;
    }

    /// <summary>注册回测任务跨进程通知发布者；Redis 配置存在用 RedisBacktestTaskNotifier，否则降级 Null。</summary>
    public static IServiceCollection AddBacktestTaskNotifier(this IServiceCollection services, bool redisAvailable)
    {
        if (redisAvailable)
            services.AddSingleton<IBacktestTaskNotifier, RedisBacktestTaskNotifier>();
        else
            services.AddSingleton<IBacktestTaskNotifier, NullBacktestTaskNotifier>();
        return services;
    }

    /// <summary>Worker 端：注册 BacktestTaskListener 订阅跨进程通知。要求 Redis 已配置。</summary>
    public static IServiceCollection AddBacktestTaskListener(this IServiceCollection services)
    {
        services.AddHostedService<BacktestTaskListener>();
        return services;
    }
}
