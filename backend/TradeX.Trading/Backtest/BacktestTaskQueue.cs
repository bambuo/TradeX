using System.Threading.Channels;

namespace TradeX.Trading.Backtest;

public class BacktestTaskQueue : IBacktestTaskQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid taskId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(taskId, ct);

    public ValueTask<Guid> ReadAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAsync(ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct);
}
