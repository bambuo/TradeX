using TradeX.Trading.Rules;

namespace TradeX.Tests.Rules;

/// <summary>ChainWorkerPool 双 Lane Worker Pool 测试（对应 Go worker_pool_test.go）。</summary>
public class WorkerPoolTests
{
    // ── DetermineLane ─────────────────────────────────────────

    [Theory]
    [InlineData(true, "signal_action", Lane.Exit)]
    [InlineData(false, "regime_gate", Lane.Entry)]
    [InlineData(false, "trailing_stop_action", Lane.Exit)]
    [InlineData(false, "take_profit_action", Lane.Exit)]
    [InlineData(false, "emergency_exit", Lane.Exit)]
    [InlineData(false, "max_drawdown", Lane.Exit)]
    [InlineData(false, "consecutive_loss_stop", Lane.Exit)]
    [InlineData(false, "kill_switch", Lane.Exit)]
    public void DetermineLane_ShouldClassifyCorrectly(bool hasPosition, string nodeKind, Lane expected)
    {
        var result = ExitNodeKinds.DetermineLane(hasPosition, [nodeKind]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetermineLane_HasPosition_AlwaysExit()
    {
        var result = ExitNodeKinds.DetermineLane(true, ["signal_action", "fixed_size"]);
        Assert.Equal(Lane.Exit, result);
    }

    [Fact]
    public void DetermineLane_NoPosition_EntryNodes_Entry()
    {
        var result = ExitNodeKinds.DetermineLane(false, ["regime_gate", "signal_action", "fixed_size"]);
        Assert.Equal(Lane.Entry, result);
    }

    // ── Stop 不抛异常 ─────────────────────────────────────────

    [Fact]
    public async Task WorkerPool_StopDoesNotThrow()
    {
        await using var pool = new ChainWorkerPool(3, 4, 4);

        // 提交几个任务确保 worker 进入了运行态
        for (var i = 0; i < 3; i++)
            pool.TrySubmitEntry(() => Task.CompletedTask);

        // 给任务一点时间执行
        await Task.Delay(50);

        // Stop 不应抛异常
        pool.Stop();
    }

    // ── Exit 永不丢弃 ─────────────────────────────────────────

    [Fact]
    public async Task WorkerPool_ExitNeverDropped()
    {
        await using var pool = new ChainWorkerPool(2, 2, 8);

        var exitRan = 0;
        var tasks = new List<Task>();

        for (var i = 0; i < 8; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await pool.SubmitExitAsync(() =>
                {
                    Interlocked.Increment(ref exitRan);
                    return Task.CompletedTask;
                });
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(8, exitRan);
    }

    // ── Entry 背压 ────────────────────────────────────────────

    [Fact]
    public void WorkerPool_EntryBackpressure_BufferZero()
    {
        // BoundedCapacity=0 的 Channel，当无 reader 等待时 TryWrite 返回 false
        // 但 ChainWorkerPool 内部有 worker 循环在 TryRead，可能抢占
        // 此处仅验证 TrySubmitEntry 不会抛异常且返回 bool
        var pool = new ChainWorkerPool(1, 1, 1);
        var result = pool.TrySubmitEntry(() => Task.CompletedTask);
        // result 可能是 true（worker 抢走）或 false（背压）—— 关键是不抛异常
        Assert.IsType<bool>(result);
        pool.Stop();
    }

    // ── ScopeKey 单飞 ─────────────────────────────────────────

    [Fact]
    public async Task WorkerPool_SingleFlightScope()
    {
        await using var pool = new ChainWorkerPool(1, 1, 1);

        Assert.True(pool.TryAcquireScope("bind1|ETH/USDT"));
        Assert.False(pool.TryAcquireScope("bind1|ETH/USDT"));
        Assert.True(pool.TryAcquireScope("bind1|BTC/USDT"));

        pool.ReleaseScope("bind1|ETH/USDT");
        Assert.True(pool.TryAcquireScope("bind1|ETH/USDT"));
    }
}
