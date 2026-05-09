using System.Collections.Concurrent;
using System.Threading.Channels;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class TaskAnalysisStore
{
    private readonly ConcurrentDictionary<Guid, List<BacktestKlineAnalysis>> _store = new();
    private readonly ConcurrentDictionary<Guid, Channel<BacktestKlineAnalysis>> _channels = new();

    public void Init(Guid taskId)
    {
        _store[taskId] = [];
        _channels[taskId] = Channel.CreateUnbounded<BacktestKlineAnalysis>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Push(Guid taskId, BacktestKlineAnalysis item)
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

    public List<BacktestKlineAnalysis>? Get(Guid taskId)
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

    public IAsyncEnumerable<BacktestKlineAnalysis> SubscribeAsync(Guid taskId, CancellationToken ct = default)
    {
        if (_channels.TryGetValue(taskId, out var ch))
            return ch.Reader.ReadAllAsync(ct);
        return AsyncEnumerable.Empty<BacktestKlineAnalysis>();
    }

    public bool Exists(Guid taskId)
    {
        return _store.ContainsKey(taskId);
    }
}
