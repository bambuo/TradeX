using System.Collections.Concurrent;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>ChainCoordinator 缓存，按 binding ID 缓存预编译的协调器。</summary>
public sealed class CoordinatorCache
{
    private readonly ConcurrentDictionary<string, ChainCoordinator> _cache = new();
    private readonly NodeRegistry _registry;

    public CoordinatorCache(NodeRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>获取或创建 ChainCoordinator。</summary>
    public ChainCoordinator GetOrCreate(string key, List<ChainDefinition> definitions)
    {
        return _cache.GetOrAdd(key, _ => new ChainCoordinator(definitions, _registry));
    }

    /// <summary>清除指定 key 的缓存。</summary>
    public void Invalidate(string key) => _cache.TryRemove(key, out _);

    /// <summary>清除所有缓存。</summary>
    public void InvalidateAll() => _cache.Clear();
}
