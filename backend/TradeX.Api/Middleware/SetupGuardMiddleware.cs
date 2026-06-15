using Microsoft.EntityFrameworkCore;
using TradeX.Core.Enums;
using TradeX.Core.ErrorCodes;
using TradeX.Infrastructure.Data;

namespace TradeX.Api.Middleware;

public class SetupGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TradeXDbContext db)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? "";

        var isSetupOrHealthPath = path == "/health"
            || path.StartsWith("/api/v1/setup/", StringComparison.OrdinalIgnoreCase);

        var isApiPath = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
        var isHubPath = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);

        if (isSetupOrHealthPath || (!isApiPath && !isHubPath))
        {
            await next(context);
            return;
        }

        var isInitialized = await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin);

        if (!isInitialized)
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(ApiResponse.Error(BusinessErrorCode.SystemNotInitialized, "系统尚未初始化").ToJson());
            return;
        }

        await next(context);
    }
}
