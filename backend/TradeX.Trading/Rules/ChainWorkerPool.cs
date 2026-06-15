using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TradeX.Trading.Rules;

/// <summary>区分入场和出场任务队列（R3）。</summary>
public enum Lane { Entry, Exit }

/// <summary>进场任务可丢（backpressure 时跳过），出场任务不可丢（止损/熔断必须执行）。</summary>
public sealed class ChainWorkerPool : IAsyncDisposable
{
    private readonly Channel<WorkerTask> _entryCh;
    private readonly Channel<WorkerTask> _exitCh;
    private readonly ConcurrentDictionary<string, bool> _scopeBusy = new();
    private readonly int _workers;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workerTasks;

    public ChainWorkerPool(int workers, int entrySize, int exitSize)
    {
        _workers = workers;
        _entryCh = Channel.CreateBounded<WorkerTask>(new BoundedChannelOptions(entrySize)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _exitCh = Channel.CreateBounded<WorkerTask>(new BoundedChannelOptions(exitSize)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _workerTasks = new Task[workers];
        for (var i = 0; i < workers; i++)
            _workerTasks[i] = RunWorkerAsync();
    }

    private async Task RunWorkerAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            WorkerTask task;
            // 非阻塞优先消费 Exit
            if (_exitCh.Reader.TryRead(out task))
            {
                await ExecuteTaskAsync(task);
                continue;
            }

            // 阻塞等待任一队列
            await Task.WhenAny(
                _exitCh.Reader.WaitToReadAsync(ct).AsTask(),
                _entryCh.Reader.WaitToReadAsync(ct).AsTask()
            );

            if (_exitCh.Reader.TryRead(out task))
            {
                await ExecuteTaskAsync(task);
            }
            else if (_entryCh.Reader.TryRead(out task))
            {
                await ExecuteTaskAsync(task);
            }
        }
    }

    private static async Task ExecuteTaskAsync(WorkerTask task)
    {
        try
        {
            await task.Fn();
            task.Done.TrySetResult();
        }
        catch (Exception ex)
        {
            task.Done.TrySetException(ex);
        }
    }

    /// <summary>提交入场任务。队列满时返回 false（backpressure 丢弃）。</summary>
    public bool TrySubmitEntry(Func<Task> fn)
    {
        var done = new TaskCompletionSource();
        var task = new WorkerTask { Lane = Lane.Entry, Fn = fn, Done = done };
        return _entryCh.Writer.TryWrite(task);
    }

    /// <summary>提交出场任务。阻塞等待队列空间，永不丢弃。</summary>
    public async ValueTask SubmitExitAsync(Func<Task> fn, CancellationToken ct = default)
    {
        var done = new TaskCompletionSource();
        var task = new WorkerTask { Lane = Lane.Exit, Fn = fn, Done = done };
        await _exitCh.Writer.WriteAsync(task, ct);
        await done.Task;
    }

    /// <summary>尝试获取同 ScopeKey 单飞锁（R2）。</summary>
    public bool TryAcquireScope(string scopeKey) =>
        _scopeBusy.TryAdd(scopeKey, true);

    /// <summary>释放 ScopeKey 单飞锁。</summary>
    public void ReleaseScope(string scopeKey) =>
        _scopeBusy.TryRemove(scopeKey, out _);

    public void Stop() => _cts.Cancel();

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Task.WhenAll(_workerTasks);
        _cts.Dispose();
    }
}

internal sealed class WorkerTask
{
    public Lane Lane { get; set; }
    public Func<Task> Fn { get; set; } = null!;
    public TaskCompletionSource Done { get; set; } = null!;
}

/// <summary>出场类节点 Kind 集合，用于 Lane 判定。</summary>
public static class ExitNodeKinds
{
    public static readonly HashSet<string> Kinds =
    [
        "trailing_stop_action", "take_profit_action", "emergency_exit",
        "max_drawdown", "consecutive_loss_stop", "kill_switch",
    ];

    /// <summary>根据持仓和链内节点决定任务 Lane。</summary>
    public static Lane DetermineLane(bool hasPosition, IEnumerable<string> nodeKinds)
    {
        if (hasPosition) return Lane.Exit;
        if (nodeKinds.Any(k => Kinds.Contains(k))) return Lane.Exit;
        return Lane.Entry;
    }
}
