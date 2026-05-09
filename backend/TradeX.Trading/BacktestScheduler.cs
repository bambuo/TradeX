using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class BacktestScheduler(
    IServiceScopeFactory scopeFactory,
    IBacktestTaskQueue queue,
    ResourceMonitor resourceMonitor,
    IOptions<BacktestSchedulerSettings> settings,
    ILogger<BacktestScheduler> logger,
    TaskAnalysisStore analysisStore) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BacktestScheduler 启动, MaxConcurrency={Max}", settings.Value.MaxConcurrency);

        await RecoverStuckTasksAsync(stoppingToken);

        var workers = new Task[settings.Value.MaxConcurrency];
        for (var i = 0; i < workers.Length; i++)
        {
            var workerIndex = i;
            workers[i] = RunWorkerLoopAsync(workerIndex, stoppingToken);
        }

        await Task.WhenAny(workers);
    }

    private async Task RunWorkerLoopAsync(int workerIndex, CancellationToken stoppingToken)
    {
        logger.LogDebug("Worker[{Index}] 启动", workerIndex);

        while (!stoppingToken.IsCancellationRequested)
        {
            while (!resourceMonitor.TryAcquire())
            {
                if (stoppingToken.IsCancellationRequested) return;
                await Task.Delay(200, stoppingToken);
            }

            Guid taskId;
            try
            {
                taskId = await queue.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                resourceMonitor.Release();
                return;
            }

            logger.LogInformation("Worker[{Index}] 开始处理任务: TaskId={TaskId}", workerIndex, taskId);
            await ProcessTaskWithTimeoutAsync(taskId, workerIndex, stoppingToken);

            resourceMonitor.Release();
        }
    }

    private async Task ProcessTaskWithTimeoutAsync(Guid taskId, int workerIndex, CancellationToken stoppingToken)
    {
        var timeout = TimeSpan.FromMinutes(settings.Value.TaskTimeoutMinutes);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
        var ct = linkedCts.Token;

        try
        {
            await ProcessTaskAsync(taskId, ct);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("Worker[{Index}] 回测任务超时: TaskId={TaskId}, Timeout={Timeout}", workerIndex, taskId, timeout);
            await FailTaskAsync(taskId, "回测执行超时", ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Worker[{Index}] 回测任务被取消: TaskId={TaskId}", workerIndex, taskId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Worker[{Index}] 回测任务异常: TaskId={TaskId}", workerIndex, taskId);
            await FailTaskAsync(taskId, ex.Message, ct);
        }
    }

    private async Task ProcessTaskAsync(Guid taskId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
        var strategyRepo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var exchangeRepo = scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
        var clientFactory = scope.ServiceProvider.GetRequiredService<IExchangeClientFactory>();

        var task = await taskRepo.GetByIdAsync(taskId, ct);
        if (task is null)
        {
            logger.LogWarning("回测任务不存在: TaskId={TaskId}", taskId);
            return;
        }

        task.Status = BacktestTaskStatus.Running;
        task.Phase = BacktestPhase.Queued;
        await taskRepo.UpdateAsync(task, ct);

        var strategy = await strategyRepo.GetByIdAsync(task.StrategyId, ct);
        if (strategy is null)
            throw new InvalidOperationException($"策略不存在: {task.StrategyId}");

        var exchange = await exchangeRepo.GetByIdAsync(task.ExchangeId, ct);
        if (exchange is null)
            throw new InvalidOperationException($"交易所不存在: {task.ExchangeId}");

        task.Phase = BacktestPhase.FetchingData;
        await taskRepo.UpdateAsync(task, ct);

        // Public kline API — no API key needed
        var klineReader = clientFactory.CreateClient(exchange.Type, "", "");
        List<Candle> candles;
        try
        {
            candles = await FetchAllKlinesAsync(klineReader, task.Pair, task.Timeframe, task.StartAt, task.EndAt, ct);
        }
        finally
        {
            if (klineReader is IDisposable d) d.Dispose();
        }

        task.Phase = BacktestPhase.Running;
        await taskRepo.UpdateAsync(task, ct);

        analysisStore.Init(task.Id);

        var engine = new BacktestEngine();
        var (result, trades, analysis) = engine.Run(strategy, task.Pair, candles, task.InitialCapital, task.PositionSize,
            a => analysisStore.Push(task.Id, a), task.Timeframe);

        await taskRepo.AddKlineAnalysesAsync(task.Id, analysis, ct);
        logger.LogInformation("回测分析数据已写入 SQLite: TaskId={TaskId}, Count={Count}", task.Id, analysis.Count);

        var resultWithTask = new BacktestResult
        {
            TaskId = task.Id,
            StrategyName = task.StrategyName,
            Pair = task.Pair,
            Timeframe = task.Timeframe,
            StartAt = task.StartAt,
            EndAt = task.EndAt,
            InitialCapital = task.InitialCapital,
            FinalValue = result.FinalValue,
            TotalReturnPercent = result.TotalReturnPercent,
            AnnualizedReturnPercent = result.AnnualizedReturnPercent,
            MaxDrawdownPercent = result.MaxDrawdownPercent,
            WinRate = result.WinRate,
            TotalTrades = result.TotalTrades,
            SharpeRatio = result.SharpeRatio,
            ProfitLossRatio = result.ProfitLossRatio,
            Details = result.Details
        };
        await taskRepo.AddResultAsync(resultWithTask, ct);

        task.Status = BacktestTaskStatus.Completed;
        task.Phase = null;
        task.CompletedAt = DateTime.UtcNow;
        await taskRepo.UpdateAsync(task, ct);

        analysisStore.Remove(task.Id);

        logger.LogInformation("回测完成: TaskId={TaskId}, Trades={TradeCount}, Return={Return}%",
            task.Id, result.TotalTrades, result.TotalReturnPercent);
    }

    private async Task RecoverStuckTasksAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
            var queue = scope.ServiceProvider.GetRequiredService<IBacktestTaskQueue>();

            var allTasks = await taskRepo.GetByStrategyIdAsync(Guid.Empty, ct);
            var stuckTasks = allTasks.Where(t => t.Status == BacktestTaskStatus.Running).ToList();

            foreach (var task in stuckTasks)
            {
                logger.LogWarning("恢复卡死的回测任务: TaskId={TaskId}, Strategy={Strategy}", task.Id, task.StrategyName);
                task.Status = BacktestTaskStatus.Pending;
                await taskRepo.UpdateAsync(task, ct);
                await queue.EnqueueAsync(task.Id, ct);
            }

            if (stuckTasks.Count > 0)
                logger.LogInformation("已恢复 {Count} 个卡死的回测任务", stuckTasks.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "恢复卡死任务失败");
        }
    }

    private async Task FailTaskAsync(Guid taskId, string reason, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();

            var task = await taskRepo.GetByIdAsync(taskId, ct);
            if (task is not null)
            {
                task.Status = BacktestTaskStatus.Failed;
                task.CompletedAt = DateTime.UtcNow;
                await taskRepo.UpdateAsync(task, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "标记回测失败时出错: TaskId={TaskId}", taskId);
        }
    }

    private static readonly Dictionary<string, long> IntervalMs = new()
    {
        ["1m"] = 60_000,
        ["5m"] = 300_000,
        ["15m"] = 900_000,
        ["30m"] = 1_800_000,
        ["1h"] = 3_600_000,
        ["4h"] = 14_400_000,
        ["1d"] = 86_400_000,
    };

    private static long GetIntervalMs(string timeframe) =>
        IntervalMs.TryGetValue(timeframe, out var ms) ? ms : 60_000;

    private static async Task<List<Candle>> FetchAllKlinesAsync(IExchangeClient client, string pair, string timeframe, DateTime startAt, DateTime endAt, CancellationToken ct)
    {
        List<Candle> allKlines = [];
        var currentStart = startAt;
        var intervalMs = GetIntervalMs(timeframe);

        while (currentStart < endAt && !ct.IsCancellationRequested)
        {
            var chunk = await client.GetKlinesAsync(pair, timeframe, currentStart, endAt, ct);
            if (chunk.Length == 0) break;

            var newKlines = chunk
                .Where(c => c.Timestamp >= currentStart && !allKlines.Any(ex => ex.Timestamp == c.Timestamp))
                .ToList();

            if (newKlines.Count == 0) break;

            allKlines.AddRange(newKlines);

            var lastTime = newKlines[^1].Timestamp;
            if (lastTime >= endAt - TimeSpan.FromMilliseconds(intervalMs)) break;

            currentStart = lastTime.AddMilliseconds(intervalMs);
        }

        return allKlines;
    }
}
