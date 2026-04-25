using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class BacktestService(
    IServiceScopeFactory scopeFactory,
    IBacktestTaskQueue queue,
    ILogger<BacktestService> logger) : IBacktestService
{
    public async Task<BacktestTask> StartBacktestAsync(Guid strategyId, Guid exchangeId, string symbolId, string timeframe, DateTime startUtc, DateTime endUtc, decimal initialCapital = 1000m, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var strategyRepo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();

        var strategy = await strategyRepo.GetByIdAsync(strategyId, ct);
        if (strategy is null)
            throw new ArgumentException($"策略不存在: {strategyId}");

        if (string.IsNullOrWhiteSpace(strategy.EntryConditionJson) || strategy.EntryConditionJson == "{}")
            throw new ArgumentException("策略缺少入场条件");

        var task = new BacktestTask
        {
            Id = Guid.NewGuid(),
            StrategyId = strategyId,
            ExchangeId = exchangeId,
            StrategyName = strategy.Name,
            SymbolId = symbolId,
            Timeframe = timeframe,
            InitialCapital = initialCapital,
            Status = BacktestTaskStatus.Pending,
            StartAtUtc = startUtc,
            EndAtUtc = endUtc,
            CreatedBy = strategy.CreatedBy
        };

        await taskRepo.AddAsync(task, ct);
        await queue.EnqueueAsync(task.Id, ct);

        logger.LogInformation("回测任务已入队: TaskId={TaskId}, Strategy={Strategy}", task.Id, strategy.Name);

        return task;
    }

    public async Task<BacktestTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
        return await taskRepo.GetByIdAsync(taskId, ct);
    }

    public async Task<BacktestResult?> GetResultAsync(Guid taskId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
        return await taskRepo.GetResultByTaskIdAsync(taskId, ct);
    }

    public async Task<List<BacktestTask>> GetTasksByStrategyAsync(Guid strategyId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
        return await taskRepo.GetByStrategyIdAsync(strategyId, ct);
    }
}
