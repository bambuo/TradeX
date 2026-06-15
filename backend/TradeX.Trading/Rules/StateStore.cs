using System.Text.Json;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>
/// fail-closed 有状态节点存储（设计文档 §2.8）。
///
/// 安全红线：
///   ST-01：状态读取必须 fail-closed —— 存储不可达时，绝不能把"读失败"误判为"无限制"
///   ST-02：状态写入失败必须可观测（上抛 error），不能静默吞掉
/// </summary>
public sealed class StateStore
{
    private readonly IStateKV _kv;
    private readonly Func<DateTime> _now;

    public StateStore(IStateKV kv) : this(kv, () => DateTime.UtcNow) { }

    internal StateStore(IStateKV kv, Func<DateTime> now)
    {
        _kv = kv;
        _now = now;
    }

    // ─── 公共 API ───

    /// <summary>
    /// 读取节点状态。
    /// 键不存在 → 空状态 + null error（安全）
    /// 其它任何错误 → 上抛异常，绝不返回空状态（fail-closed，ST-01）
    /// 懒过期：ExpiresAt 已过 → 视为空状态，异步清理
    /// </summary>
    public async Task<NodeState> ReadStateAsync(string scopeKey, string nodeKind, CancellationToken ct = default)
    {
        var k = Key(scopeKey, nodeKind);
        var (data, err) = await _kv.GetAsync(k, ct);

        if (err == StateKVError.KeyNotFound)
            return new NodeState(); // 真·空状态

        if (err != StateKVError.None)
        {
            // ❌ 不可吞！存储不可达必须上抛，由调用方 fail-closed（拒单 / 视同熔断）
            throw new InvalidOperationException($"Read state {scopeKey}/{nodeKind}: {err}");
        }

        var st = JsonSerializer.Deserialize<NodeState>(data!)
            ?? throw new InvalidOperationException($"Decode state {scopeKey}/{nodeKind}: null result");

        // 懒过期：ExpiresAt 已过 → 视为空状态，异步清理
        if (st.ExpiresAt is not null && _now() > st.ExpiresAt.Value)
        {
            _ = _kv.DelAsync(k, ct); // 异步清理，不阻塞
            return new NodeState();
        }

        return st;
    }

    /// <summary>
    /// 写入节点状态；序列化或写入失败都必须上抛（ST-02）。
    /// </summary>
    public async Task WriteStateAsync(string scopeKey, string nodeKind, NodeState st, CancellationToken ct = default)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(st);
        var err = await _kv.SetAsync(Key(scopeKey, nodeKind), data, TimeSpan.FromDays(7), ct);

        if (err != StateKVError.None)
        {
            // 风控计数写失败 = 该笔亏损没记下，下一轮会越限。必须上抛。
            throw new InvalidOperationException($"Write state {scopeKey}/{nodeKind}: {err}");
        }
    }

    /// <summary>
    /// Risk 节点 fail-closed 消费状态示例：
    ///   读状态出错 → (false, error) 拒绝交易（安全）
    ///   无记录/已过期 → (true, nil) 放行
    ///   仍在冷却窗口内 → (false, nil) 正常拦截
    /// </summary>
    public async Task<(bool Allowed, string? Error)> CooldownAllowsAsync(string scopeKey, CancellationToken ct = default)
    {
        var st = await ReadStateAsync(scopeKey, "cooldown", ct);
        if (st.ExpiresAt is not null) // ReadState 未清掉 → 仍在冷却窗口
            return (false, null);
        return (true, null);
    }

    // ─── helpers ───

    private static string Key(string scopeKey, string nodeKind) => $"chainstate:{scopeKey}:{nodeKind}";
}

// ═══════════════════════════════════════════════════════════════════════════
// 底层 KV 抽象
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>KV 存储错误码。</summary>
public enum StateKVError
{
    None = 0,
    KeyNotFound = 1,
    ConnectionFailed = 2,
    Timeout = 3,
    Unknown = 99,
}

/// <summary>
/// 底层键值存储抽象。真实实现注入 Redis 适配器；测试注入 fake。
/// 约定：Get 在键不存在时返回 (null, KeyNotFound)，其它错误如实返回。
/// </summary>
public interface IStateKV
{
    Task<(byte[]? Data, StateKVError Error)> GetAsync(string key, CancellationToken ct = default);
    Task<StateKVError> SetAsync(string key, byte[] value, TimeSpan ttl, CancellationToken ct = default);
    Task<StateKVError> DelAsync(string key, CancellationToken ct = default);
}


