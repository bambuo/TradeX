namespace TradeX.Trading.Backtest;

public interface IBacktestTaskQueue
{
    ValueTask EnqueueAsync(Guid taskId, CancellationToken ct = default);
    ValueTask<Guid> ReadAsync(CancellationToken ct = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default);
}
