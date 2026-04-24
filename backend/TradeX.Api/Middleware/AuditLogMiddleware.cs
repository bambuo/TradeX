using System.Security.Claims;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Middleware;

public class AuditLogMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAuditLogRepository repo)
    {
        await next(context);

        if (context.Request.Method == "GET")
            return;

        var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var entry = new AuditLogEntry
        {
            UserId = userIdStr is not null ? Guid.Parse(userIdStr) : null,
            Action = $"{context.Request.Method} {context.Request.Path}",
            Resource = context.Request.Path,
            IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };

        await repo.AddAsync(entry);
    }
}
