namespace TradeX.Trading.Rules;

/// <summary>
/// 在途防抖：单飞锁 + pending 预占（设计文档 §2.8）。
///
/// 两层防护：
///   1. 同 ScopeKey 单飞：一个 binding|pair 在"评估+执行"闭环内不接受并发新轮（消除并发自竞争）
///   2. pending 预占：决策产出时（执行前）即写短 TTL pending；下游门控把 pending 视同"冷却中"，
///      堵住"异步执行尚未确认成交、下一轮又下单"的 TOCTOU 窗口
/// </summary>
public sealed class SingleFlightCoordinator
{
    private readonly Gate _gate = new();
    private readonly CooldownStore _cooldown;
    private readonly TimeSpan _cooldownDuration;

    private long _dispatched;

    public long Dispatched => Interlocked.Read(ref _dispatched);

    public SingleFlightCoordinator(bool usePending, TimeSpan cooldownDuration)
    {
        _cooldown = new CooldownStore(usePending);
        _cooldownDuration = cooldownDuration;
    }

    /// <summary>同步评估：单飞锁横跨整个评估+执行（含成交确认）。</summary>
    public bool EvaluateSync(string scopeKey)
    {
        if (!_gate.TryAcquire(scopeKey))
            return false; // 已有在途评估 → 丢弃本轮

        try
        {
            if (_cooldown.Active(scopeKey))
                return false;

            _cooldown.Reserve(scopeKey);          // 执行前预占
            Interlocked.Increment(ref _dispatched); // 发单
            _cooldown.Confirm(scopeKey, _cooldownDuration); // 成交确认
            return true;
        }
        finally
        {
            _gate.Release(scopeKey);
        }
    }

    /// <summary>异步评估：发单后立刻释放单飞锁，成交确认延后。pending 预占堵 TOCTOU 窗口。</summary>
    public bool EvaluateAsyncDispatch(string scopeKey, Action<string> onDispatch)
    {
        if (!_gate.TryAcquire(scopeKey))
            return false;

        if (_cooldown.Active(scopeKey))
        {
            _gate.Release(scopeKey);
            return false;
        }

        _cooldown.Reserve(scopeKey);           // pending：执行前预占
        Interlocked.Increment(ref _dispatched); // 异步发单
        onDispatch(scopeKey);                  // 调用方注册成交回调
        _gate.Release(scopeKey);               // 成交确认前释放单飞锁
        return true;
    }

    /// <summary>显式触发某 ScopeKey 的成交确认。</summary>
    public void CompleteFill(string scopeKey)
    {
        _cooldown.Confirm(scopeKey, _cooldownDuration);
    }

    /// <summary>取消 pending（下单失败回滚）。</summary>
    public void CancelPending(string scopeKey)
    {
        _cooldown.Cancel(scopeKey);
    }

    // ─── Gate: 同 ScopeKey 单飞锁 ───

    private sealed class Gate
    {
        private readonly object _mu = new();
        private readonly HashSet<string> _busy = [];

        public bool TryAcquire(string key)
        {
            lock (_mu)
            {
                if (!_busy.Add(key)) return false;
                return true;
            }
        }

        public void Release(string key)
        {
            lock (_mu)
            {
                _busy.Remove(key);
            }
        }
    }

    // ─── CooldownStore: pending 预占 + 冷却 ───

    private sealed class CooldownStore
    {
        private readonly object _mu = new();
        private readonly Dictionary<string, DateTime> _until = [];
        private readonly Dictionary<string, bool> _pending = [];
        private readonly bool _usePending;

        public CooldownStore(bool usePending)
        {
            _usePending = usePending;
        }

        public bool Active(string key)
        {
            lock (_mu)
            {
                if (_usePending && _pending.GetValueOrDefault(key))
                    return true;

                return _until.TryGetValue(key, out var t) && DateTime.UtcNow < t;
            }
        }

        public void Reserve(string key)
        {
            lock (_mu)
            {
                _pending[key] = true;
            }
        }

        public void Confirm(string key, TimeSpan duration)
        {
            lock (_mu)
            {
                _until[key] = DateTime.UtcNow + duration;
                _pending[key] = false;
            }
        }

        public void Cancel(string key)
        {
            lock (_mu)
            {
                _pending[key] = false;
            }
        }
    }
}
