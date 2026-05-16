using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Backtest;

namespace TradeX.Trading;

public class BacktestService(
    IStrategyRepository strategyRepo,
    IBacktestTaskRepository taskRepo,
    IBacktestTaskQueue queue,
    IBacktestTaskNotifier notifier,
    ILogger<BacktestService> logger) : IBacktestService
{
    public async Task<BacktestTask> StartBacktestAsync(Guid strategyId, Guid exchangeId, string pair, string timeframe, DateTime startAt, DateTime endAt, decimal initialCapital, decimal? positionSize = null, CancellationToken ct = default)
    {
        var strategy = await strategyRepo.GetByIdAsync(strategyId, ct);
        if (strategy is null)
            throw new ArgumentException($"策略不存在: {strategyId}");

        if (string.IsNullOrWhiteSpace(strategy.EntryCondition) || strategy.EntryCondition == "{}")
            throw new ArgumentException("策略缺少入场条件");

        var task = new BacktestTask
        {
            Id = Guid.NewGuid(),
            StrategyId = strategyId,
            ExchangeId = exchangeId,
            StrategyName = strategy.Name,
            Pair = pair,
            Timeframe = timeframe,
            InitialCapital = initialCapital,
            PositionSize = positionSize,
            Status = BacktestTaskStatus.Pending,
            StartAt = startAt,
            EndAt = endAt,
            CreatedBy = strategy.CreatedBy
        };

        await taskRepo.AddAsync(task, ct);
        await queue.EnqueueAsync(task.Id, ct);
        // 跨进程通知（API → Worker）；Redis 未配置时为 no-op，Worker 端兜底扫描会捡起
        await notifier.NotifyTaskQueuedAsync(task.Id, ct);

        logger.LogInformation("回测任务已入队: TaskId={TaskId}, Strategy={Strategy}", task.Id, strategy.Name);

        return task;
    }

    public async Task<BacktestTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default)
        => await taskRepo.GetByIdAsync(taskId, ct);

    public async Task<BacktestResult?> GetResultAsync(Guid taskId, CancellationToken ct = default)
        => await taskRepo.GetResultByTaskIdAsync(taskId, ct);

    public async Task<List<BacktestTask>> GetTasksByStrategyAsync(Guid strategyId, CancellationToken ct = default)
        => await taskRepo.GetByStrategyIdAsync(strategyId, ct);
}
