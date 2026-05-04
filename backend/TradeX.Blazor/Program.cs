using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.FluentUI.AspNetCore.Components;
using TradeX.Api.Services;
using TradeX.Api.Settings;
using TradeX.Blazor.Components;
using TradeX.Blazor.Services;
using TradeX.Core.Interfaces;
using TradeX.Exchange;
using TradeX.Infrastructure;
using TradeX.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthWorkflowService>();
builder.Services.AddScoped<ExchangePageService>();
builder.Services.AddScoped<TraderPageService>();
builder.Services.AddScoped<ITraderPageService>(sp =>
    AuditProxy<ITraderPageService, TraderPageService>.Create(
        sp.GetRequiredService<TraderPageService>(),
        sp.GetRequiredService<IAuditLogRepository>()));
builder.Services.AddScoped<StrategyPageService>();
builder.Services.AddScoped<IStrategyPageService>(sp =>
    AuditProxy<IStrategyPageService, StrategyPageService>.Create(
        sp.GetRequiredService<StrategyPageService>(),
        sp.GetRequiredService<IAuditLogRepository>()));
builder.Services.AddScoped<StrategyTemplatePageService>();
builder.Services.AddScoped<IStrategyTemplatePageService>(sp =>
    AuditProxy<IStrategyTemplatePageService, StrategyTemplatePageService>.Create(
        sp.GetRequiredService<StrategyTemplatePageService>(),
        sp.GetRequiredService<IAuditLogRepository>()));
builder.Services.AddScoped<AuditLogPageService>();
builder.Services.AddSingleton<AuthTicketStore>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<MfaService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.Cookie.Name = "TradeX.Blazor.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });
builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddExchange();

var encryptionKey = builder.Configuration.GetSection("Encryption")["Key"]!;
builder.Services.AddEncryption(encryptionKey);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/auth/complete", async (string ticket, AuthTicketStore ticketStore, HttpContext context) =>
{
    var tokens = ticketStore.Redeem(ticket);
    if (tokens is null)
    {
        return Results.Redirect("/login");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, tokens.UserId.ToString()),
        new(ClaimTypes.Name, tokens.UserName),
        new(ClaimTypes.Role, tokens.Role)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    var properties = new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
        AllowRefresh = true
    };

    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    return Results.Redirect("/");
});

app.MapGet("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradeXDbContext>();
    await DbInitializer.InitializeAsync(db);
}

app.Run();
