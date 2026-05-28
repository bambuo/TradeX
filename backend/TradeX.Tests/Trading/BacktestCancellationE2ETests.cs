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
/// 锁定 commit 9d57701 取消路径的关键不变量:
///   1) CancelBacktestAsync 只对非终态任务生效, 写 Status=Cancelled
///   2) TryAdvancePhaseAsync 检测到 Cancelled 立即返回 false, 不脏写
///   3) PollForCancellationAsync 周期性查 DB, 一旦发现 Cancelled 触发 engineCts
/// </summary>
public class BacktestCancellationE2ETests
{
    private static BacktestService BuildService(IBacktestTaskRepository taskRepo)
    {
        var strategyRepo = Substitute.For<IStrategyRepository>();
        var queue = Substitute.For<IBacktestTaskQueue>();
        var notifier = Substitute.For<IBacktestTaskNotifier>();
        var logger = Substitute.For<ILogger<BacktestService>>();
        return new BacktestService(strategyRepo, taskRepo, queue, notifier, logger);
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
        // 关键不变量: 用户在 worker 推进 Phase 前发起 Cancel, 写入 Cancelled.
        // TryAdvancePhaseAsync 重读后应放弃推进, 不允许把 Status 改回 Running.
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

    [Fact]
    public async Task PollForCancellationAsync_DetectsCancelledStatus_TriggersCts()
    {
        // 模拟 worker 跑引擎期间, 用户在 DB 把 Status 改成 Cancelled.
        // poller 在下一次 1s 轮询时检测到, 触发 engineCts.
        var taskId = Guid.NewGuid();
        var repo = Substitute.For<IBacktestTaskRepository>();
        repo.GetByIdAsync(taskId, Arg.Any<CancellationToken>())
            .Returns(new BacktestTask { Id = taskId, Status = BacktestTaskStatus.Cancelled });

        var scopeFactory = BuildScopeFactory(repo);

        using var engineCts = new CancellationTokenSource();
        var stoppingCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await InvokePollForCancellationAsync(scopeFactory, taskId, engineCts, stoppingCts.Token);

        Assert.True(engineCts.IsCancellationRequested);
    }

    [Fact]
    public async Task PollForCancellationAsync_StaysQuiet_UntilExternallyCancelled()
    {
        // DB 持续 Running, poller 不应触发 engineCts; 外部 stoppingToken 取消则 poller 正常退出
        var repo = Substitute.For<IBacktestTaskRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BacktestTask { Id = Guid.NewGuid(), Status = BacktestTaskStatus.Running });

        var scopeFactory = BuildScopeFactory(repo);
        using var engineCts = new CancellationTokenSource();
        using var stoppingCts = new CancellationTokenSource();

        var pollerTask = InvokePollForCancellationAsync(scopeFactory, Guid.NewGuid(), engineCts, stoppingCts.Token);
        await Task.Delay(200);
        Assert.False(engineCts.IsCancellationRequested);

        stoppingCts.Cancel();
        await pollerTask;
        Assert.False(engineCts.IsCancellationRequested);
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
            new TaskAnalysisStore())!;

        var method = typeof(BacktestScheduler).GetMethod("TryAdvancePhaseAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(BacktestScheduler), "TryAdvancePhaseAsync");
        var task = method.Invoke(scheduler, [taskRepo, taskId, status, phase, CancellationToken.None]) as Task<bool>
            ?? throw new InvalidOperationException("返回类型不匹配");
        return await task;
    }

    private static async Task InvokePollForCancellationAsync(
        IServiceScopeFactory scopeFactory, Guid taskId, CancellationTokenSource engineCts, CancellationToken stoppingToken)
    {
        var method = typeof(BacktestScheduler).GetMethod("PollForCancellationAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(BacktestScheduler), "PollForCancellationAsync");
        var task = method.Invoke(null, [scopeFactory, taskId, engineCts, stoppingToken]) as Task
            ?? throw new InvalidOperationException("返回类型不匹配");
        await task;
    }

    private static IServiceScopeFactory BuildScopeFactory(IBacktestTaskRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
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
