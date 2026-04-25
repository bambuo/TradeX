namespace TradeX.Trading;

public interface IBacktestTaskQueue
{
    ValueTask EnqueueAsync(Guid taskId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default);
}
