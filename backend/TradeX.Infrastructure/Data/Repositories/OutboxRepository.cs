using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class OutboxRepository(TradeXDbContext context) : IOutboxRepository
{
    public async Task EnqueueAsync(OutboxEvent evt, CancellationToken ct = default)
    {
        await context.OutboxEvents.AddAsync(evt, ct);
        // 不在这里 SaveChanges — 调用方负责事务边界
    }

    public async Task<List<OutboxEvent>> PickPendingAsync(int batchSize, CancellationToken ct = default)
    {
        return await context.OutboxEvents
            .AsTracking()
            .Where(e => e.Status == OutboxStatus.Pending)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task MarkSentAsync(long id, CancellationToken ct = default)
    {
        var row = await context.OutboxEvents.AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return;
        row.Status = OutboxStatus.Sent;
        row.PublishedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(long id, string error, int maxAttempts, CancellationToken ct = default)
    {
        var row = await context.OutboxEvents.AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return;
        row.AttemptCount += 1;
        row.LastError = error;
        if (row.AttemptCount >= maxAttempts)
            row.Status = OutboxStatus.Failed;
        // 否则保持 Pending，下一轮再试
        await context.SaveChangesAsync(ct);
    }
}
