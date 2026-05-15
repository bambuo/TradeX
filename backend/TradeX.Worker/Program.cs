using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TradeX.Exchange;
using TradeX.Indicators;
using TradeX.Infrastructure;
using TradeX.Notifications;
using TradeX.Trading;
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

    // 临时事件总线：仅打日志，前端实时事件暂缺；阶段 3 会替换为 RedisEventBus
    builder.Services.AddSingleton<ITradingEventBus, LoggingEventBus>();

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
