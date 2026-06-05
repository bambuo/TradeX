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
    string Timestamp);

public sealed class GetAuditLogsUseCase(
    IAuditLogRepository auditLogRepo,
    IUserRepository userRepo)
    : IUseCase<GetAuditLogsQuery, Result<List<AuditLogDto>>>
{
    public async Task<Result<List<AuditLogDto>>> ExecuteAsync(GetAuditLogsQuery query, CancellationToken ct = default)
    {
        var (items, _) = await auditLogRepo.GetPagedAsync(
            query.Page, query.PageSize,
            query.UserId?.ToString(), query.Action,
            null, null, null, ct);

        var userIds = items.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().ToList();
        var usernameMap = new Dictionary<Guid, string>();
        foreach (var uid in userIds)
        {
            var user = await userRepo.GetByIdAsync(uid, ct);
            if (user is not null)
                usernameMap[uid] = user.Username;
        }

        var dtos = items.Select(a => new AuditLogDto(
            a.Id, a.UserId,
            a.UserId.HasValue && usernameMap.TryGetValue(a.UserId.Value, out var un) ? un : null,
            a.Action, a.Resource,
            a.Detail, a.IpAddress, a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))).ToList();

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
