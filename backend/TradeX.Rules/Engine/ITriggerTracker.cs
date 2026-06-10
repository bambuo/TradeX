using System.Collections.Concurrent;

namespace TradeX.Rules.Engine;

/// <summary>
/// 规则触发时间追踪器，用于 MinInterval 冷却判定。
/// <para>
/// 键由调用方（<see cref="RuleEvaluator"/>）按 "{scopeKey}/{ruleCode}" 组合，
/// 从而隔离不同策略绑定 / 交易对 / 回测会话——同名规则 Code 不再互相串扰。
/// </para>
/// <para>
/// 时间由调用方显式传入（实盘为 IClock.UtcNow，回测为 K 线时间），
/// 追踪器自身不读墙钟，确保回测的 MinInterval 按模拟时间推进。
/// </para>
/// </summary>
public interface ITriggerTracker
{
    /// <summary>返回 <paramref name="now"/> 距该键上次触发已过去的秒数。从未触发过则返回 null。</summary>
    double? ElapsedSecondsSinceLastTrigger(string key, DateTime now);

    /// <summary>记录该键在 <paramref name="now"/> 触发。</summary>
    void RecordTrigger(string key, DateTime now);
}

/// <summary>
/// 默认实现。持有 ConcurrentDictionary，跨所有 Scope 共享触发记录（实盘进程级单例）。
/// 回测应使用独立实例以避免与实盘状态互相污染。
/// </summary>
public sealed class TriggerTracker : ITriggerTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _lastTriggerTimes = [];

    public double? ElapsedSecondsSinceLastTrigger(string key, DateTime now)
    {
        if (_lastTriggerTimes.TryGetValue(key, out var last))
            return (now - last).TotalSeconds;
        return null;
    }

    public void RecordTrigger(string key, DateTime now)
    {
        _lastTriggerTimes[key] = now;
    }
}
