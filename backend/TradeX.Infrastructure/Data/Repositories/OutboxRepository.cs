using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class OutboxRepository(TradeXDbContext context) : IOutboxRepository
{
    public async Task EnqueueAsync(OutboxEvent evt, CancellationToken ct = default)
    {
        await context.OutboxEvents.AddAsync(evt, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

    public async Task<List<OutboxEvent>> PickPendingAsync(int batchSize, CancellationToken ct = default)
    {
        // WorkerSingleInstanceGuard 已保证 Worker 单实例，无需数据库行锁。
        // 纯标准 LINQ，数据库无关。
        return await context.OutboxEvents
            .Where(e => e.Status == OutboxStatus.Pending)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(batchSize)
            .AsTracking()
            .ToListAsync(ct);
    }

    public async Task MarkSentAsync(Guid id, CancellationToken ct = default)
    {
        var row = await context.OutboxEvents.AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return;
        row.Status = OutboxStatus.Sent;
        row.PublishedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    /// <summary>批量标记为已发送，减少事务次数。</summary>
    public async Task MarkSentBatchAsync(List<Guid> ids, CancellationToken ct = default)
    {
        var rows = await context.OutboxEvents
            .AsTracking()
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.Status = OutboxStatus.Sent;
            row.PublishedAtUtc = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<bool> MarkFailedAsync(Guid id, string error, int maxAttempts, CancellationToken ct = default)
    {
        var row = await context.OutboxEvents.AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return false;
        row.AttemptCount += 1;
        row.LastError = error;
        if (row.AttemptCount >= maxAttempts)
            row.Status = OutboxStatus.Failed;
        await context.SaveChangesAsync(ct);
        return row.Status == OutboxStatus.Failed;
    }
}
