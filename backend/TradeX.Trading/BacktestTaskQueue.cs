using System.Threading.Channels;

namespace TradeX.Trading;

public class BacktestTaskQueue : IBacktestTaskQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid taskId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(taskId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct);
}
