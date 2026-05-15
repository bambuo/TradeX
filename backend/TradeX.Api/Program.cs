using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TradeX.Api.Hubs;
using TradeX.Api.Middleware;
using TradeX.Api.Services;
using TradeX.Api.Settings;
using TradeX.Exchange;
using TradeX.Indicators;
using TradeX.Infrastructure;
using TradeX.Infrastructure.Data;
using TradeX.Notifications;
using TradeX.Trading;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/tradex-.log", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/tradex-.log", rollingInterval: RollingInterval.Day));

    var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

    builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
            };
        });

    builder.Services.AddAuthorization();

    builder.Services.AddSingleton<JwtService>();
    builder.Services.AddSingleton<MfaService>();
    builder.Services.AddSingleton<ITradingEventBus, SignalREventBus>();

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    var mySqlVersion = builder.Configuration["Database:MySqlServerVersion"] ?? "8.4.0";
    builder.Services.AddInfrastructure(connectionString, mySqlVersion);

    var encryptionKey = builder.Configuration.GetSection("Encryption")["Key"]!;
    builder.Services.AddEncryption(encryptionKey);

    builder.Services.AddExchange();
    builder.Services.AddIndicators();
    builder.Services.AddTrading();
    builder.Services.AddNotifications();

    builder.Services.AddSignalR();

    builder.Services.AddScoped<TradeX.Api.Filters.MfaActionFilter>();
    builder.Services.AddControllers(o => o.Filters.AddService<TradeX.Api.Filters.MfaActionFilter>())
        .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
    builder.Services.AddSwaggerGen();

    // OpenTelemetry: 业务指标 + 自动埋点 (ASP.NET Core / HttpClient / EF Core / Runtime)
    // Metrics → Prometheus 抓取 (/metrics)；Traces → OTLP (OTEL_EXPORTER_OTLP_ENDPOINT 未设时静默)
    builder.Services.AddSingleton<TradeX.Trading.Observability.TradeXMetrics>();
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("tradex-api", serviceVersion: "1.0.0"))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(TradeX.Trading.Observability.TradeXMetrics.MeterName)
            .AddMeter("Polly")
            .AddPrometheusExporter())
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
                tracing.AddOtlpExporter();
        });

    var app = builder.Build();

    // 配置 IP 白名单
    var ipWhitelistEnabled = builder.Configuration.GetValue<bool>("Security:IpWhitelist:Enabled");
    if (ipWhitelistEnabled)
    {
        var allowedIps = builder.Configuration.GetSection("Security:IpWhitelist:AllowedCidr").Get<string[]>() ?? [];
        IpWhitelistMiddleware.Configure(true, allowedIps);
    }

    app.UseSerilogRequestLogging();

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    if (ipWhitelistEnabled)
        app.UseMiddleware<IpWhitelistMiddleware>();
    app.UseMiddleware<SetupGuardMiddleware>();

    // 静态文件无需认证 — 必须在 auth 之前注册
    // SPA HTML 禁止浏览器缓存 — 确保 JS 资源 hash 变更后即时更新
    app.Use(async (context, next) =>
    {
        await next(context);
        if (context.Response.ContentType?.StartsWith("text/html") == true)
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }
    });

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapFallbackToFile("index.html");

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<CasbinAuthorizationMiddleware>();
    app.UseMiddleware<AuditLogMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Prometheus 抓取端点 (默认 /metrics)
    app.MapPrometheusScrapingEndpoint();

    app.MapControllers();
    app.MapHub<TradingHub>("/hubs/trading");

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<TradeXDbContext>();
        await DbInitializer.InitializeAsync(db);
    }

    Log.Information("TradeX 启动完成, Environment={Env}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TradeX 启动失败");
}
finally
{
    Log.CloseAndFlush();
}
