using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TradeX.Infrastructure;
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

    // DB（与 API 共享同一 schema/迁移；同时连接，靠乐观并发 token 防竞争）
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    var mySqlVersion = builder.Configuration["Database:MySqlServerVersion"] ?? "8.4.0";
    builder.Services.AddInfrastructure(connectionString, mySqlVersion);

    var encryptionKey = builder.Configuration.GetSection("Encryption")["Key"]!;
    builder.Services.AddEncryption(encryptionKey);

    // OTel：业务指标 + 自动埋点。Prometheus 用独立 HttpListener 暴露 /metrics:9464
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

    // 阶段 1：仅注册 Heartbeat 占位，验证启动流程
    // 阶段 2 替换为 AddTradingWorker()（TradingEngine + BacktestScheduler + OrderReconciler 等）
    builder.Services.AddHostedService<HeartbeatService>();

    var host = builder.Build();
    Log.Information("TradeX.Worker 启动: Environment={Env}", builder.Environment.EnvironmentName);
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
