using System.Collections.Concurrent;
using System.Threading.Channels;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class TaskAnalysisStore
{
    private readonly ConcurrentDictionary<Guid, List<BacktestCandleAnalysis>> _store = new();
    private readonly ConcurrentDictionary<Guid, Channel<BacktestCandleAnalysis>> _channels = new();

    public void Init(Guid taskId)
    {
        _store[taskId] = [];
        _channels[taskId] = Channel.CreateUnbounded<BacktestCandleAnalysis>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Push(Guid taskId, BacktestCandleAnalysis item)
    {
        if (_store.TryGetValue(taskId, out var list))
        {
            lock (list)
            {
                list.Add(item);
            }
        }
        if (_channels.TryGetValue(taskId, out var ch))
            ch.Writer.TryWrite(item);
    }

    public List<BacktestCandleAnalysis>? Get(Guid taskId)
    {
        return _store.TryGetValue(taskId, out var list) ? list : null;
    }

    public int Count(Guid taskId)
    {
        return _store.TryGetValue(taskId, out var list) ? list.Count : 0;
    }

    public void Remove(Guid taskId)
    {
        _store.TryRemove(taskId, out _);
        if (_channels.TryRemove(taskId, out var ch))
            ch.Writer.TryComplete();
    }

    public IAsyncEnumerable<BacktestCandleAnalysis> SubscribeAsync(Guid taskId, CancellationToken ct = default)
    {
        if (_channels.TryGetValue(taskId, out var ch))
            return ch.Reader.ReadAllAsync(ct);
        return AsyncEnumerable.Empty<BacktestCandleAnalysis>();
    }

    public bool Exists(Guid taskId)
    {
        return _store.ContainsKey(taskId);
    }
}
