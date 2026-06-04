using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TradeX.Exchange;
using TradeX.Indicators;
using TradeX.Infrastructure;
using TradeX.Infrastructure.Data;
using TradeX.Trading;
using TradeX.BacktestWorker;

// Serilog bootstrap logger（DI 启动前用）
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/backtest-worker-.log", rollingInterval: RollingInterval.Day)
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
        .WriteTo.File("logs/backtest-worker-.log", rollingInterval: RollingInterval.Day));

    // ------ Infrastructure ------
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    builder.Services.AddInfrastructure(connectionString);

    var encryptionKey = builder.Configuration.GetSection("Encryption")["Key"]!;
    builder.Services.AddEncryption(encryptionKey);

    // ------ Trading 共享服务 ------
    builder.Services.AddExchange();
    builder.Services.AddIndicators();
    builder.Services.AddTradingShared();

    // ------ 单实例锁 ------
    builder.Services.AddHostedService<BacktestWorkerSingleInstanceGuard>();

    // ------ 回测 Worker 独占服务 ------
    var redisConn = builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(redisConn))
    {
        builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
            _ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));

        builder.Services.AddTradingBacktestWorker();
        Log.Information("回测 Worker 事件驱动: Redis Streams → {Tasks} / {Cancellations}",
            TradeX.Trading.Backtest.BacktestChannels.Tasks,
            TradeX.Trading.Backtest.BacktestChannels.Cancellations);
    }
    else
    {
        Log.Warning("回测 Worker: 未配置 Redis:ConnectionString，回测调度降级为 DB 轮询模式");
        builder.Services.AddTradingBacktestWorker(redisAvailable: false);
    }

    // ------ Observability ------
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("tradex-backtest-worker", serviceVersion: "1.0.0"))
        .WithMetrics(m =>
        {
            m.AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusHttpListener(opts =>
                    opts.UriPrefixes = [builder.Configuration["Otel:PrometheusListener"] ?? "http://*:9465/"]);
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

    Log.Information("TradeX.BacktestWorker 启动: Environment={Env} — BacktestScheduler / BacktestCancellationConsumer 已注册",
        builder.Environment.EnvironmentName);
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "回测 Worker 启动失败");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
