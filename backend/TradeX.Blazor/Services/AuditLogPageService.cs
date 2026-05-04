using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public sealed class AuditLogPageService(IAuditLogRepository auditLogRepo)
{
    public async Task<(IReadOnlyList<AuditLogEntryView> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? userId, string? action, string? resourceType,
        DateTime? startUtc, DateTime? endUtc, CancellationToken ct = default)
    {
        var (items, total) = await auditLogRepo.GetPagedAsync(
            page, pageSize, userId, action, resourceType, startUtc, endUtc, ct);

        var views = items.Select(e => new AuditLogEntryView(
            e.Id, e.UserId, FormatAction(e.Action), e.Resource, e.ResourceId,
            e.Detail, e.IpAddress, e.Timestamp)).ToArray();

        return (views, total);
    }

    private static string FormatAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return "-";

        // AuditProxy 已存干净 Label（如 "新建交易员"），直接返回
        if (!action.Contains('|'))
            return action;

        // 旧格式 "METHOD|label"（来自中间件或旧代码）
        var parts = action.Split('|');
        return parts.Length == 2 ? parts[1] : action;
    }
}

public sealed record AuditLogEntryView(
    Guid Id,
    Guid? UserId,
    string Action,
    string Resource,
    string? ResourceId,
    string? Detail,
    string IpAddress,
    DateTime Timestamp);
