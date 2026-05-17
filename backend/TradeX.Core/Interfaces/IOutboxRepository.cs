using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IOutboxRepository
{
    /// <summary>写入 outbox 行；调用方需要在同一 DbContext 事务内调用 SaveChangesAsync 才能与业务一起提交。</summary>
    Task EnqueueAsync(OutboxEvent evt, CancellationToken ct = default);

    /// <summary>取一批待发布事件，按 CreatedAt 升序；同时被多 relay 实例拿到时靠乐观锁防重。</summary>
    Task<List<OutboxEvent>> PickPendingAsync(int batchSize, CancellationToken ct = default);

    /// <summary>标记成功。</summary>
    Task MarkSentAsync(long id, CancellationToken ct = default);

    /// <summary>标记失败 + 增加重试次数。超过 maxAttempts 后置 Failed 状态。</summary>
    Task MarkFailedAsync(long id, string error, int maxAttempts, CancellationToken ct = default);
}
