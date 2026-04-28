using System.Security.Claims;
using System.Text.Json;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Middleware;

public class AuditLogMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task InvokeAsync(HttpContext context, IAuditLogRepository repo)
    {
        await next(context);

        if (context.Request.Method == "GET")
            return;

        var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var (resource, resourceId, actionLabel) = ParsePath(context.Request.Method, context.Request.Path);
        var detail = JsonSerializer.Serialize(new { method = context.Request.Method, path = context.Request.Path.Value, statusCode = context.Response.StatusCode, ip = context.Connection.RemoteIpAddress?.ToString() }, JsonOptions);

        var entry = new AuditLogEntry
        {
            UserId = userIdStr is not null ? Guid.Parse(userIdStr) : null,
            Action = actionLabel,
            Resource = resource,
            ResourceId = resourceId,
            Detail = detail,
            IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };

        await repo.AddAsync(entry);
    }

    private static (string resource, string? resourceId, string action) ParsePath(string method, string path)
    {
        var segments = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return ("系统", null, $"{method} {path}");

        // api/{resource}/{id?}/{sub?}/{subId?}
        if (segments[0] != "api") return ("系统", null, $"{method} {path}");

        var resource = segments[1];
        var id = segments.Length > 2 ? segments[2] : null;
        var sub = segments.Length > 3 ? segments[3] : null;
        var subId = segments.Length > 4 ? segments[4] : null;

        var verb = method switch
        {
            "POST" => "创建",
            "PUT" => "更新",
            "DELETE" => "删除",
            "PATCH" => "修改",
            _ => method
        };

        var resourceName = resource switch
        {
            "traders" => "交易员",
            "exchanges" => "交易所",
            "strategies" => "策略",
            "settings" => "系统设置",
            "users" => "用户",
            "auth" => "认证",
            "orders" => "订单",
            "positions" => "持仓",
            "notifications" => "通知渠道",
            _ => resource
        };

        if (sub == "toggle")
            return (resourceName, id, id is not null ? "启用/禁用" : "切换状态");

        if (sub == "test" || sub == "backtests")
        {
            var subAction = sub == "backtests" ? "回测" : "测试连接";
            return (resourceName, id, $"执行{subAction}");
        }

        if (resource == "auth")
        {
            var authActions = new Dictionary<string, string>
            {
                ["login"] = "登录", ["logout"] = "登出", ["refresh"] = "刷新令牌",
                ["mfa/setup"] = "设置 MFA", ["mfa/verify"] = "验证 MFA",
                ["send-recovery-codes"] = "发送恢复码"
            };
            var subPath = string.Join('/', segments.Skip(2));
            var label = authActions.TryGetValue(subPath, out var a) ? a : subPath;
            return ("认证", null, label);
        }

        if (method == "POST" && sub is null && id is null)
            return (resourceName, id, $"新建{resourceName}");
        if (method == "POST" && sub is null && id is not null)
            return (resourceName, id, verb);

        if (method == "DELETE" && sub is null)
            return (resourceName, id, $"删除{resourceName}");
        if (method == "PUT" && sub is null)
            return (resourceName, id, $"更新{resourceName}");

        var subName = sub switch
        {
            "strategies" => "策略",
            "exchanges" => "交易所",
            "orders" => "订单",
            "positions" => "持仓",
            "manual" => "手动下单",
            "role" => "变更角色",
            "channels" => "通知渠道",
            "channels/*" => "通知渠道",
            _ => sub
        };

        return (resourceName, id, $"{verb}{subName}");
    }
}
