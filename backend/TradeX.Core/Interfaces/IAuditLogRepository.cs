using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken ct = default);
    Task<List<AuditLogEntry>> GetRecentAsync(int count = 100, CancellationToken ct = default);
    Task<(List<AuditLogEntry> Items, int Total)> GetPagedAsync(int page = 1, int pageSize = 20,
        string? userId = null, string? action = null, string? resourceType = null,
        DateTime? startUtc = null, DateTime? endUtc = null, CancellationToken ct = default);
}
