using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class AuditLogRepository(TradeXDbContext context) : IAuditLogRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        context.AuditLogs.Add(entry);
        await context.SaveChangesAsync(ct);
    }

    public async Task<List<AuditLogEntry>> GetRecentAsync(int count = 100, CancellationToken ct = default) =>
        await context.AuditLogs.OrderByDescending(x => x.Timestamp).Take(count).ToListAsync(ct);

    public async Task<(List<AuditLogEntry> Items, int Total)> GetPagedAsync(int page = 1, int pageSize = 20,
        string? userId = null, string? action = null, string? resourceType = null,
        DateTime? startUtc = null, DateTime? endUtc = null, CancellationToken ct = default)
    {
        var query = context.AuditLogs.AsQueryable();

        if (userId is not null && Guid.TryParse(userId, out var uid))
            query = query.Where(x => x.UserId == uid);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(resourceType))
            query = query.Where(x => x.Resource == resourceType);
        if (startUtc.HasValue)
            query = query.Where(x => x.Timestamp >= startUtc.Value);
        if (endUtc.HasValue)
            query = query.Where(x => x.Timestamp <= endUtc.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
