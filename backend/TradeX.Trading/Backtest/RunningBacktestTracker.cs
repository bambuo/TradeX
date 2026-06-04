using System.Collections.Concurrent;

namespace TradeX.Trading.Backtest;

/// <summary>
/// 跟踪当前正在运行的回测任务及其 CancellationTokenSource。
/// 由 <see cref="BacktestScheduler"/> 注册/注销，<see cref="BacktestCancellationConsumer"/> 读取并触发取消。
/// 替代 <c>PollForCancellationAsync</c> 的 DB 轮询方案。
/// </summary>
public sealed class RunningBacktestTracker
{
    public ConcurrentDictionary<Guid, CancellationTokenSource> RunningTasks { get; } = new();
}
