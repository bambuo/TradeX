using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using TradeX.Core.Attributes;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public class AuditProxy<TInterface, TImpl> : DispatchProxy
    where TInterface : class
    where TImpl : class, TInterface
{
    private TImpl _target = null!;
    private IAuditLogRepository _auditLogRepo = null!;

    public static TInterface Create(TImpl target, IAuditLogRepository auditLogRepo)
    {
        var proxy = Create<TInterface, AuditProxy<TInterface, TImpl>>();
        // Create<TInterface, TProxy>() returns TInterface but the runtime object is AuditProxy
        var typed = (AuditProxy<TInterface, TImpl>)(object)proxy;

        var targetField = typeof(AuditProxy<TInterface, TImpl>)
            .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!;
        targetField.SetValue(typed, target);

        var repoField = typeof(AuditProxy<TInterface, TImpl>)
            .GetField("_auditLogRepo", BindingFlags.Instance | BindingFlags.NonPublic)!;
        repoField.SetValue(typed, auditLogRepo);

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null) return null;

        var attr = targetMethod.GetCustomAttribute<AuditActionAttribute>();
        var isVoid = targetMethod.ReturnType == typeof(void);
        var isTask = targetMethod.ReturnType == typeof(Task) || targetMethod.ReturnType.BaseType == typeof(Task);

        object? result;
        try
        {
            result = targetMethod.Invoke(_target, args);
        }
        catch (TargetInvocationException ex)
        {
            if (attr is not null)
                TryLogAudit(attr, args, null, ex.InnerException?.Message);
            throw ex.InnerException!;
        }

        if (attr is not null && !isTask)
        {
            TryLogAudit(attr, args, result, null);
            return result;
        }

        if (attr is not null && result is Task task)
        {
            return AwaitAndLog(task, attr, args);
        }

        if (attr is not null && result is Task<object> genericTask)
        {
            return AwaitAndLogGeneric(genericTask, attr, args);
        }

        return result;
    }

    private async Task AwaitAndLog(Task task, AuditActionAttribute attr, object?[]? args)
    {
        try
        {
            await task;
            TryLogAudit(attr, args, null, null);
        }
        catch (Exception ex)
        {
            TryLogAudit(attr, args, null, ex.Message);
            throw;
        }
    }

    private async Task<T> AwaitAndLogGeneric<T>(Task<T> task, AuditActionAttribute attr, object?[]? args)
    {
        T result;
        try
        {
            result = await task;
            TryLogAudit(attr, args, result, null);
        }
        catch (Exception ex)
        {
            TryLogAudit(attr, args, null, ex.Message);
            throw;
        }
        return result;
    }

    private void TryLogAudit(AuditActionAttribute attr, object?[]? args, object? result, string? error)
    {
        try
        {
            var resourceId = ExtractResourceId(args, result);
            var userId = ExtractUserId(args);
            var detail = BuildDetail(args, result, error);

            var entry = new AuditLogEntry
            {
                UserId = userId,
                Action = attr.Label ?? $"{attr.Method} {attr.Resource}",
                Resource = attr.Resource,
                ResourceId = resourceId?.ToString(),
                Detail = detail,
                IpAddress = "Blazor",
                Timestamp = DateTime.UtcNow
            };

            _ = _auditLogRepo.AddAsync(entry, CancellationToken.None);
        }
        catch
        {
            // 审计日志失败不影响业务
        }
    }

    private static Guid? ExtractUserId(object?[]? args)
    {
        if (args is null) return null;
        foreach (var arg in args)
        {
            if (arg is ClaimsPrincipal principal)
            {
                var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return Guid.TryParse(value, out var id) ? id : null;
            }
        }
        return null;
    }

    private static Guid? ExtractResourceId(object?[]? args, object? result)
    {
        if (result is Guid g && g != Guid.Empty) return g;

        if (args is not null)
        {
            foreach (var arg in args)
            {
                if (arg is Guid guidArg && guidArg != Guid.Empty)
                    return guidArg;
            }
        }

        if (result is not null)
        {
            var idProp = result.GetType().GetProperty("Id");
            if (idProp?.GetValue(result) is Guid resultId && resultId != Guid.Empty)
                return resultId;
        }

        return null;
    }

    private static string? BuildDetail(object?[]? args, object? result, string? error)
    {
        try
        {
            var items = new Dictionary<string, object?>();

            if (args is not null)
            {
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i] is not null and not ClaimsPrincipal and not CancellationToken and not Guid)
                        items[$"arg{i}"] = args[i]?.ToString();
                }
            }

            if (result is not null and not Guid and not bool and not int and not long and not string)
                items["result"] = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });

            if (error is not null)
                items["error"] = error;

            return items.Count > 0 ? JsonSerializer.Serialize(items) : null;
        }
        catch
        {
            return null;
        }
    }
}
