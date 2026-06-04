using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeX.Core.Interfaces;
using TradeX.Trading.Backtest;
using TradeX.Trading.Commands;
using TradeX.Trading.Engine;
using TradeX.Trading.Execution;
using TradeX.Trading.Migration;
using TradeX.Trading.Outbox;
using TradeX.Trading.Risk;
using TradeX.Trading.Streaming;

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
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
        services.AddScoped<IConditionTreeEvaluator, ConditionTreeEvaluator>();
        services.AddScoped<IStrategyDecisionEngine, StrategyDecisionEngine>();
        services.AddScoped<ConditionTreeValidator>();
        services.AddScoped<IPortfolioRiskManager, PortfolioRiskManager>();
        services.AddScoped<IFillProjector, FillProjector>();
        services.AddScoped<ITradeExecutor, TradeExecutor>();
        services.AddScoped<IOrderReconciler, OrderReconciler>();
        services.AddScoped<IPositionReconciler, PositionReconciler>();
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddScoped<BacktestEngine>();
        services.AddScoped<LegacyStrategyScanner>();

        services.AddSingleton<IKillSwitch, KillSwitch>();
        services.AddSingleton<OrderBookSlippageGuard>();
        services.AddSingleton<Execution.PairRuleCache>();
        services.AddSingleton<Execution.KlineGapDetector>();
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
    /// Worker 进程独占：启动 StrategyEvaluationConsumer 策略评估循环 / ResourceMonitor 资源采样。
    /// 同时注册 Trade 和 Kline 事件流通道及 StreamManager。
    /// 注意：回测相关服务已移至 <see cref="AddTradingBacktestWorker"/>，请勿在此重复注册。
    /// </summary>
    private const int TradeEventChannelCapacity = 1000;
    private const int KlineEventChannelCapacity = 100;

    public static IServiceCollection AddTradingWorker(this IServiceCollection services)
    {
        services.AddSingleton<IResourceProvider, SystemResourceProvider>();
        services.AddSingleton<ResourceMonitor>();

        // Trade 逐笔成交事件通道
        services.AddSingleton(_ => Channel.CreateBounded<TradeEvent>(new BoundedChannelOptions(TradeEventChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        }));
        services.AddSingleton<TradeStreamManager>();

        // K 线收盘事件通道（独立于 Trade，因推送频率低且需要不同订阅参数）
        services.AddSingleton(_ => Channel.CreateBounded<KlineEvent>(new BoundedChannelOptions(KlineEventChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        }));
        services.AddSingleton<KlineStreamManager>();

        services.AddSingleton<StrategyEvaluationConsumer>();
        services.AddHostedService<ResourceMonitor>(sp => sp.GetRequiredService<ResourceMonitor>());
        services.AddHostedService<OrderReconcilerService>();
        services.AddHostedService(sp => sp.GetRequiredService<StrategyEvaluationConsumer>());
        return services;
    }

    /// <summary>
    /// 回测 Worker（TradeX.BacktestWorker）独占：注册回测调度引擎、取消事件消费者、任务监听器。
    /// 与 <see cref="AddTradingWorker"/> 分开注册，确保回测（CPU 密集）与实盘交易引擎进程隔离。
    /// </summary>
    public static IServiceCollection AddTradingBacktestWorker(this IServiceCollection services, bool redisAvailable = true)
    {
        services.AddSingleton<IResourceProvider, SystemResourceProvider>();
        services.AddSingleton<ResourceMonitor>();

        services.AddSingleton<IBacktestTaskQueue, BacktestTaskQueue>();
        services.AddSingleton<TaskAnalysisStore>();
        services.AddSingleton<RunningBacktestTracker>();

        services.AddHostedService<BacktestScheduler>();

        if (redisAvailable)
        {
            services.AddHostedService<BacktestTaskListener>();
            services.AddHostedService<BacktestCancellationConsumer>();
        }

        return services;
    }

    /// <summary>
    /// 注册 Worker 端的命令处理器集合 + Redis 订阅者。调用方需确保 <c>IConnectionMultiplexer</c> 已注册。
    /// 调用顺序：先注册自定义 handler（实现 <see cref="IWorkerCommandHandler"/>），再调用本方法。
    /// </summary>
    public static IServiceCollection AddTradingWorkerCommandBus(this IServiceCollection services)
    {
        services.AddSingleton<IWorkerCommandHandler, ReconcileNowHandler>();
        services.AddSingleton<IWorkerCommandHandler, RefreshSubscriptionsHandler>();
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

    /// <summary>
    /// 注册回测任务跨进程通知发布者（API 端使用）；Redis 配置存在用 RedisBacktestTaskNotifier，否则降级 Null。
    /// 注意：消费端 <see cref="BacktestTaskListener"/> 已移至 <c>TradeX.BacktestWorker</c> 进程。
    /// </summary>
    public static IServiceCollection AddBacktestTaskNotifier(this IServiceCollection services, bool redisAvailable)
    {
        if (redisAvailable)
            services.AddSingleton<IBacktestTaskNotifier, RedisBacktestTaskNotifier>();
        else
            services.AddSingleton<IBacktestTaskNotifier, NullBacktestTaskNotifier>();
        return services;
    }

    /// <summary>
    /// 注册回测取消事件发布者（API 端使用）；与任务通知同条件。
    /// 注意：消费端 <see cref="BacktestCancellationConsumer"/> 已移至 <c>TradeX.BacktestWorker</c> 进程。
    /// </summary>
    public static IServiceCollection AddBacktestCancellationNotifier(this IServiceCollection services, bool redisAvailable)
    {
        if (redisAvailable)
            services.AddSingleton<IBacktestCancellationNotifier, RedisBacktestCancellationNotifier>();
        else
            services.AddSingleton<IBacktestCancellationNotifier, NullBacktestCancellationNotifier>();
        return services;
    }

    /// <summary>Worker 端：注册 BacktestTaskListener 订阅跨进程通知。要求 Redis 已配置。</summary>
    public static IServiceCollection AddBacktestTaskListener(this IServiceCollection services)
    {
        services.AddHostedService<BacktestTaskListener>();
        return services;
    }

    /// <summary>
    /// Worker 端：注册 OutboxRelayService —— 后台轮询 outbox_events 表把 Pending 事件推到 Redis。
    /// 调用方应同时使用 <see cref="OutboxTradingEventBus"/> 作为 ITradingEventBus 的实现。
    /// 要求 Redis 已配置。
    /// </summary>
    public static IServiceCollection AddOutboxRelay(this IServiceCollection services)
    {
        services.AddHostedService<OutboxRelayService>();
        return services;
    }
}
