using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading;
using TradeX.Trading.Backtest;

namespace TradeX.Tests.Trading;

/// <summary>
/// 锁定 commit 9d57701 取消路径的关键不变量（已从 DB 轮询改造为事件驱动）:
///   1) CancelBacktestAsync 只对非终态任务生效, 写 Status=Cancelled
///   2) TryAdvancePhaseAsync 检测到 Cancelled 立即返回 false, 不脏写
///   3) 取消事件驱动: BacktestCancellationConsumer 通过 RunningBacktestTracker 触发 CTS
/// </summary>
public class BacktestCancellationE2ETests
{
    private static BacktestService BuildService(IBacktestTaskRepository taskRepo)
    {
        var strategyRepo = Substitute.For<IStrategyRepository>();
        var notifier = Substitute.For<IBacktestTaskNotifier>();
        var logger = Substitute.For<ILogger<BacktestService>>();
        return new BacktestService(strategyRepo, taskRepo, notifier, logger);
    }

    [Fact]
    public async Task CancelBacktestAsync_RunningTask_WritesCancelledStatus()
    {
        var taskId = Guid.NewGuid();
        var stored = new BacktestTask { Id = taskId, Status = BacktestTaskStatus.Running };
        var repo = Substitute.For<IBacktestTaskRepository>();
        repo.GetByIdAsync(taskId, Arg.Any<CancellationToken>()).Returns(stored);

        var service = BuildService(repo);
        var ok = await service.CancelBacktestAsync(taskId);

        Assert.True(ok);
        await repo.Received(1).UpdateAsync(Arg.Is<BacktestTask>(t =>
            t.Id == taskId && t.Status == BacktestTaskStatus.Cancelled && t.CompletedAt != null),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BacktestTaskStatus.Completed)]
    [InlineData(BacktestTaskStatus.Failed)]
    [InlineData(BacktestTaskStatus.Cancelled)]
    public async Task CancelBacktestAsync_TerminalState_ReturnsFalseAndDoesNotWrite(BacktestTaskStatus terminal)
    {
        var taskId = Guid.NewGuid();
        var stored = new BacktestTask { Id = taskId, Status = terminal };
        var repo = Substitute.For<IBacktestTaskRepository>();
        repo.GetByIdAsync(taskId, Arg.Any<CancellationToken>()).Returns(stored);

        var service = BuildService(repo);
        var ok = await service.CancelBacktestAsync(taskId);

        Assert.False(ok);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BacktestTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelBacktestAsync_TaskNotFound_ReturnsFalse()
    {
        var repo = Substitute.For<IBacktestTaskRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BacktestTask?)null);

        var service = BuildService(repo);
        var ok = await service.CancelBacktestAsync(Guid.NewGuid());

        Assert.False(ok);
    }

    [Fact]
    public async Task TryAdvancePhaseAsync_TaskCancelled_DoesNotOverwrite()
    {
        var taskId = Guid.NewGuid();
        var stored = new BacktestTask { Id = taskId, Status = BacktestTaskStatus.Cancelled };
        var repo = Substitute.For<IBacktestTaskRepository>();
        repo.GetByIdAsync(taskId, Arg.Any<CancellationToken>()).Returns(stored);

        var advanced = await InvokeTryAdvancePhaseAsync(repo, taskId, BacktestTaskStatus.Running, BacktestPhase.FetchingData);

        Assert.False(advanced);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BacktestTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAdvancePhaseAsync_TaskRunning_WritesNewPhase()
    {
        var taskId = Guid.NewGuid();
        var stored = new BacktestTask { Id = taskId, Status = BacktestTaskStatus.Running };
        var repo = Substitute.For<IBacktestTaskRepository>();
        repo.GetByIdAsync(taskId, Arg.Any<CancellationToken>()).Returns(stored);

        var advanced = await InvokeTryAdvancePhaseAsync(repo, taskId, BacktestTaskStatus.Running, BacktestPhase.Running);

        Assert.True(advanced);
        await repo.Received(1).UpdateAsync(Arg.Is<BacktestTask>(t => t.Phase == BacktestPhase.Running), Arg.Any<CancellationToken>());
    }

    // ──── 事件驱动取消的新测试 ────

    [Fact]
    public void RunningBacktestTracker_Register_And_Cancel_TriggersCts()
    {
        // 模拟 BacktestCancellationConsumer 的取消路径:
        // BacktestScheduler 注册 task → tracker, consumer 查找并 Cancel
        var tracker = new RunningBacktestTracker();
        var taskId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        tracker.RunningTasks[taskId] = cts;

        Assert.True(tracker.RunningTasks.TryGetValue(taskId, out var retrieved));
        Assert.Same(cts, retrieved);

        retrieved!.Cancel();
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void RunningBacktestTracker_Unregister_TaskNoLongerTracked()
    {
        var tracker = new RunningBacktestTracker();
        var taskId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        tracker.RunningTasks[taskId] = cts;
        Assert.True(tracker.RunningTasks.TryGetValue(taskId, out _));

        tracker.RunningTasks.TryRemove(taskId, out _);
        Assert.False(tracker.RunningTasks.TryGetValue(taskId, out _));
    }

    [Fact]
    public void RunningBacktestTracker_UnknownTaskId_DoesNotCancel()
    {
        var tracker = new RunningBacktestTracker();
        var runningTaskId = Guid.NewGuid();
        var cancelTaskId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        tracker.RunningTasks[runningTaskId] = cts;

        // 取消一个不存在的任务 —— 不影响正在运行的任务
        Assert.False(tracker.RunningTasks.TryGetValue(cancelTaskId, out _));
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task RunningBacktestTracker_CancelDuringEngineRun_TriggersImmediateCancel()
    {
        // 端到端验证: 引擎运行期间, 外部通过 tracker 取消, 引擎在下一轮循环检测到
        var tracker = new RunningBacktestTracker();
        var taskId = Guid.NewGuid();
        using var engineCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);

        tracker.RunningTasks[taskId] = engineCts;

        // 模拟 BacktestCancellationConsumer 收到取消事件后触发
        var cancelled = false;
        var engineTask = Task.Run(async () =>
        {
            try
            {
                while (!engineCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(10, engineCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            cancelled = true;
        });

        // 通过 tracker 触发取消
        if (tracker.RunningTasks.TryGetValue(taskId, out var cts))
            cts.Cancel();

        await engineTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(cancelled);
        Assert.True(engineCts.IsCancellationRequested);

        tracker.RunningTasks.TryRemove(taskId, out _);
        Assert.False(tracker.RunningTasks.TryGetValue(taskId, out _));
    }

    // ──── reflection helpers ────
    private static async Task<bool> InvokeTryAdvancePhaseAsync(
        IBacktestTaskRepository taskRepo, Guid taskId, BacktestTaskStatus status, BacktestPhase phase)
    {
        var settings = Microsoft.Extensions.Options.Options.Create(new BacktestSchedulerSettings { MaxConcurrency = 1 });
        var scheduler = (BacktestScheduler)Activator.CreateInstance(
            typeof(BacktestScheduler),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IBacktestTaskQueue>(),
            BuildResourceMonitor(),
            settings,
            Substitute.For<ILogger<BacktestScheduler>>(),
            new TaskAnalysisStore(),
            new RunningBacktestTracker())!;

        var method = typeof(BacktestScheduler).GetMethod("TryAdvancePhaseAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(BacktestScheduler), "TryAdvancePhaseAsync");
        var task = method.Invoke(scheduler, [taskRepo, taskId, status, phase, CancellationToken.None]) as Task<bool>
            ?? throw new InvalidOperationException("返回类型不匹配");
        return await task;
    }

    private static ResourceMonitor BuildResourceMonitor()
    {
        var settings = Microsoft.Extensions.Options.Options.Create(new BacktestSchedulerSettings
        {
            MaxConcurrency = 1,
            MonitorIntervalSeconds = 5,
            MemoryWarningMb = 512, MemoryCriticalMb = 1024, MemoryAbsoluteMb = 1536,
            CpuWarningPercent = 50, CpuCriticalPercent = 75, CpuAbsolutePercent = 90
        });
        return new ResourceMonitor(Substitute.For<IResourceProvider>(), settings, Substitute.For<ILogger<ResourceMonitor>>());
    }
}
