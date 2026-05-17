using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TradeX.Exchange;
using TradeX.Indicators;
using TradeX.Infrastructure;
using TradeX.Notifications;
using TradeX.Trading;
using TradeX.Trading.Events;
using TradeX.Trading.Messaging;
using TradeX.Trading.Observability;

// Serilog bootstrap logger（DI 启动前用）
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day));

    // ------ Infrastructure ------
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    var mySqlVersion = builder.Configuration["Database:MySqlServerVersion"] ?? "8.4.0";
    builder.Services.AddInfrastructure(connectionString, mySqlVersion);

    var encryptionKey = builder.Configuration.GetSection("Encryption")["Key"]!;
    builder.Services.AddEncryption(encryptionKey);

    // ------ Trading 业务（共享 + Worker 独占的 HostedService） ------
    builder.Services.AddExchange();
    builder.Services.AddIndicators();
    builder.Services.AddNotifications();
    builder.Services.AddTradingShared();
    builder.Services.AddTradingWorker();

    // 事件总线：Redis 配置存在 → RedisEventBus（发布到 tradex:events，API bridge 转发 SignalR）；
    //           否则 → LoggingEventBus 降级（前端实时事件丢失但不阻塞业务）
    var redisConn = builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(redisConn))
    {
        builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
            _ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));
        // Outbox 模式：业务路径写 outbox 表（与业务事务原子），后台 relay 推到 Redis
        // 解决 RedisEventBus 直接 publish 失败 → 事件丢失的一致性漏洞
        builder.Services.AddScoped<ITradingEventBus, TradeX.Trading.Outbox.OutboxTradingEventBus>();
        builder.Services.AddOutboxRelay();
        builder.Services.AddTradingWorkerCommandBus();
        builder.Services.AddBacktestTaskNotifier(redisAvailable: true);
        builder.Services.AddBacktestTaskListener();
        Log.Information("Worker 事件总线(Outbox) + 命令总线 + 回测桥: Redis → {Events} / {Commands} / {Backtest}",
            TradingEventChannels.Events,
            TradeX.Trading.Commands.WorkerCommandChannels.Commands,
            TradeX.Trading.Backtest.BacktestChannels.Tasks);
    }
    else
    {
        builder.Services.AddSingleton<ITradingEventBus, LoggingEventBus>();
        builder.Services.AddBacktestTaskNotifier(redisAvailable: false);
        Log.Warning("Worker 事件总线/命令通道/回测桥: 未配置 Redis:ConnectionString，全部降级（仅本地进程内有效）");
    }

    // ------ Observability ------
    builder.Services.AddSingleton<TradeXMetrics>();
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("tradex-worker", serviceVersion: "1.0.0"))
        .WithMetrics(m =>
        {
            m.AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TradeXMetrics.MeterName)
                .AddMeter("Polly")
                .AddPrometheusHttpListener(opts =>
                    opts.UriPrefixes = [builder.Configuration["Otel:PrometheusListener"] ?? "http://*:9464/"]);
        })
        .WithTracing(t =>
        {
            t.AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
                t.AddOtlpExporter();
        });

    var host = builder.Build();
    Log.Information("TradeX.Worker 启动: Environment={Env} — 已装载 TradingEngine / BacktestScheduler / ResourceMonitor",
        builder.Environment.EnvironmentName);
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker 启动失败");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
