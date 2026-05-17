using System.Collections.Concurrent;
using Casbin;

namespace TradeX.Infrastructure.Casbin;

/// <summary>
/// Casbin 策略执行器。Enforcer 本身是 thread-safe 且加载 model+policy 后驻留内存。
/// 增加了 <see cref="DecisionCache"/>：缓存 (role,path,method) → bool 决策结果 30s TTL，
/// 避免每请求都过一次 model 文件 IO + 字符串 keyMatch 评估。
/// 策略文件变更后调用 <see cref="ReloadPolicy"/> 主动失效。
/// </summary>
public class CasbinEnforcer
{
    private readonly Enforcer _enforcer;
    private readonly ConcurrentDictionary<string, (bool Decision, DateTime ExpiresAtUtc)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public CasbinEnforcer()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Casbin", "model.conf");
        var policyPath = Path.Combine(AppContext.BaseDirectory, "Casbin", "policy.csv");

        if (!File.Exists(modelPath))
            modelPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TradeX.Infrastructure", "Casbin", "model.conf");
        if (!File.Exists(policyPath))
            policyPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TradeX.Infrastructure", "Casbin", "policy.csv");

        _enforcer = new Enforcer(modelPath, policyPath);
    }

    public bool Enforce(string role, string path, string method)
    {
        var key = $"{role}|{method}|{path}";
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > now)
            return cached.Decision;

        var decision = _enforcer.Enforce(role, path, method);
        _cache[key] = (decision, now + CacheTtl);

        // 简易控制内存：超过 10k 条目时清一半（粗略 LRU）
        if (_cache.Count > 10_000)
        {
            foreach (var kv in _cache.Where(kv => kv.Value.ExpiresAtUtc <= now).Take(5_000))
                _cache.TryRemove(kv.Key, out _);
        }
        return decision;
    }

    /// <summary>策略文件变更后调用，重载并清空决策缓存。</summary>
    public void ReloadPolicy()
    {
        _enforcer.LoadPolicy();
        _cache.Clear();
    }
}
