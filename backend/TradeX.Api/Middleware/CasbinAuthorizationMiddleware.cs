using System.Security.Claims;
using System.Text.Json;
using TradeX.Infrastructure.Casbin;

namespace TradeX.Api.Middleware;

public class CasbinAuthorizationMiddleware(RequestDelegate next)
{
    private static readonly string[] PublicPaths =
    [
        "/health",
        "/api/v1/setup/status",
        "/api/v1/setup/initialize",
        "/api/v1/auth/login",
        "/api/v1/auth/verify-mfa",
        "/api/v1/auth/refresh",
        "/api/v1/strategies/schema",
        "/api/v1/auth/mfa/setup",
        "/api/v1/auth/mfa/verify",
        "/api/v1/auth/send-recovery-codes"
    ];

    public async Task InvokeAsync(HttpContext context, CasbinEnforcer enforcer)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? "";

        // 放行公开路径和非 API 路径（静态文件、SPA 路由等）
        var isApiOrHub = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(path)
            || !isApiOrHub
            || PublicPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = "UNAUTHORIZED",
                message = "需要身份验证"
            }));
            return;
        }

        var role = context.User.FindFirst(ClaimTypes.Role)?.Value?.ToLowerInvariant();
        if (string.IsNullOrEmpty(role))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = "AUTH_FORBIDDEN",
                message = "无法识别用户角色"
            }));
            return;
        }

        var method = context.Request.Method;
        var allowed = enforcer.Enforce(role, path, method);

        if (!allowed)
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = "AUTH_FORBIDDEN",
                message = $"权限不足: 角色 {role} 不允许 {method} {path}"
            }));
            return;
        }

        await next(context);
    }
}
