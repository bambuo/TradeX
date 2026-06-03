using TradeX.Application.Common;
using TradeX.Core.Interfaces;

namespace TradeX.Application.AuditLogs;

public sealed record GetAuditLogsQuery(
    Guid? UserId,
    string? Action,
    int Page,
    int PageSize);

public sealed record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string? Username,
    string Action,
    string Resource,
    string? Detail,
    string IpAddress,
    DateTime Timestamp);

public sealed class GetAuditLogsUseCase(IAuditLogRepository auditLogRepo)
    : IUseCase<GetAuditLogsQuery, Result<List<AuditLogDto>>>
{
    public async Task<Result<List<AuditLogDto>>> ExecuteAsync(GetAuditLogsQuery query, CancellationToken ct = default)
    {
        var (items, _) = await auditLogRepo.GetPagedAsync(
            query.Page, query.PageSize,
            query.UserId?.ToString(), query.Action,
            null, null, null, ct);

        var dtos = items.Select(a => new AuditLogDto(
            a.Id, a.UserId, null, a.Action, a.Resource,
            a.Detail, a.IpAddress, a.Timestamp)).ToList();

        return Result<List<AuditLogDto>>.Ok(dtos);
    }
}

public sealed record GetAuditLogsCountQuery(
    Guid? UserId,
    string? Action);

public sealed class GetAuditLogsCountUseCase(IAuditLogRepository auditLogRepo)
    : IUseCase<GetAuditLogsCountQuery, Result<int>>
{
    public async Task<Result<int>> ExecuteAsync(GetAuditLogsCountQuery query, CancellationToken ct = default)
    {
        var (_, total) = await auditLogRepo.GetPagedAsync(
            1, 1,
            query.UserId?.ToString(), query.Action,
            null, null, null, ct);

        return Result<int>.Ok(total);
    }
}
