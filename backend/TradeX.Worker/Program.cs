using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TradeX.Exchange;
using TradeX.Indicators;
using TradeX.Infrastructure;
using TradeX.Infrastructure.Data;
using TradeX.Notifications;
using TradeX.Trading;
using TradeX.Trading.EventBus;
using TradeX.Trading.Observability;
using TradeX.Worker;

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
    builder.Services.AddInfrastructure(connectionString);

    var encryptionKey = builder.Configuration.GetSection("Encryption")["Key"]!;
    builder.Services.AddEncryption(encryptionKey);

    // ------ Trading 业务（共享 + Worker 独占的 HostedService） ------
    builder.Services.AddExchange();
    builder.Services.AddIndicators();
    builder.Services.AddNotifications();
    builder.Services.AddTradingShared();
    builder.Services.AddHostedService<WorkerSingleInstanceGuard>();
    builder.Services.AddTradingWorker();
    builder.Services.AddHostedService<ExchangeOrderSyncService>();

    // 事件总线：Redis 配置存在 → RedisDomainEventBus（XADD 到 tradex:events，API 端 RedisEventConsumerService 接收后转发 SignalR）；
    //           否则 → NullDomainEventBus 降级（前端实时事件丢失但不阻塞业务）
    var redisConn = builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(redisConn))
    {
        builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
            _ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));
        builder.Services.AddDomainEventBus(redisAvailable: true);
        builder.Services.AddTradingWorkerCommandBus();
        Log.Information("Worker 事件总线: Redis → {Events} / {Commands}",
            "tradex:events",
            TradeX.Trading.Commands.WorkerCommandChannels.Commands);
    }
    else
    {
        builder.Services.AddDomainEventBus(redisAvailable: false);
        Log.Warning("Worker 事件总线: 未配置 Redis:ConnectionString，全部降级（仅本地进程内有效）");
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

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<TradeXDbContext>();
        await DbInitializer.InitializeAsync(db);
    }

    Log.Information("TradeX.Worker 启动: Environment={Env} — 已装载 TradingEngine / ResourceMonitor / OrderReconciler",
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
