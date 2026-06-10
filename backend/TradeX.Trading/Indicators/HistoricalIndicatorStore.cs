using System.Collections.Concurrent;

namespace TradeX.Trading.Indicators;

/// <summary>
/// 按 scopeKey 管理指标快照的滚动缓存。
/// 实盘每次评估后记录当前指标值，供带有 lookback 的规则查询历史数据。
/// </summary>
public interface IHistoricalIndicatorStore
{
    void RecordSnapshot(string scopeKey, Dictionary<string, decimal> indicators);
    IReadOnlyList<Dictionary<string, decimal>>? GetSnapshots(string scopeKey, int maxCount);
    void Clear(string scopeKey);
}

/// <summary>
/// 进程级单例实现。内部 ConcurrentDictionary，scopeKey→环形快照队列。
/// 默认每 scope 保留最多 200 根快照。
/// </summary>
public sealed class HistoricalIndicatorStore : IHistoricalIndicatorStore
{
    private const int DefaultMaxHistory = 200;

    private readonly ConcurrentDictionary<string, List<Dictionary<string, decimal>>> _store = new(StringComparer.Ordinal);

    public void RecordSnapshot(string scopeKey, Dictionary<string, decimal> indicators)
    {
        var list = _store.GetOrAdd(scopeKey, _ => []);
        lock (list)
        {
            list.Add(new Dictionary<string, decimal>(indicators));
            if (list.Count > DefaultMaxHistory)
                list.RemoveRange(0, list.Count - DefaultMaxHistory);
        }
    }

    public IReadOnlyList<Dictionary<string, decimal>>? GetSnapshots(string scopeKey, int maxCount)
    {
        if (!_store.TryGetValue(scopeKey, out var list))
            return null;

        lock (list)
        {
            if (list.Count == 0)
                return null;

            var start = Math.Max(0, list.Count - maxCount);
            return list.GetRange(start, list.Count - start);
        }
    }

    public void Clear(string scopeKey)
    {
        _store.TryRemove(scopeKey, out _);
    }
}
