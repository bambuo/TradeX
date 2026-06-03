using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IOutboxRepository
{
    /// <summary>写入 outbox 行；调用方需要在同一事务边界内调用 <see cref="SaveChangesAsync"/> 才能与业务一起提交。</summary>
    Task EnqueueAsync(OutboxEvent evt, CancellationToken ct = default);

    /// <summary>提交当前 outbox 工作单元。</summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>取一批待发布事件，按 CreatedAt 升序；同时被多 relay 实例拿到时靠乐观锁防重。</summary>
    Task<List<OutboxEvent>> PickPendingAsync(int batchSize, CancellationToken ct = default);

    /// <summary>标记成功。</summary>
    Task MarkSentAsync(Guid id, CancellationToken ct = default);

    /// <summary>批量标记成功，减少事务次数。</summary>
    Task MarkSentBatchAsync(List<Guid> ids, CancellationToken ct = default);

    /// <summary>标记失败 + 增加重试次数。超过 maxAttempts 后置 Failed 状态。返回是否已进入终态 Failed。</summary>
    Task<bool> MarkFailedAsync(Guid id, string error, int maxAttempts, CancellationToken ct = default);
}
