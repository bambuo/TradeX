using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Backtest;

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
        var engine = scope.ServiceProvider.GetRequiredService<BacktestEngine>();

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

        var (result, trades, analysis) = engine.Run(strategy, task.Pair, candles, task.InitialCapital, task.PositionSize,
            a => analysisStore.Push(task.Id, a), task.Timeframe);

        // 引擎执行期间可能已被用户取消，重新读取最新状态
        var latestTask = await taskRepo.GetByIdAsync(task.Id, ct);
        if (latestTask?.Status == BacktestTaskStatus.Cancelled)
        {
            logger.LogInformation("回测任务已被取消，跳过写入结果: TaskId={TaskId}", task.Id);
            analysisStore.Remove(task.Id);
            return;
        }

        await taskRepo.AddKlineAnalysesAsync(task.Id, analysis, ct);
        logger.LogInformation("回测分析数据已写入数据库: TaskId={TaskId}, Count={Count}", task.Id, analysis.Count);

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

            // 1) 把上次崩溃留下的 Running 任务回滚为 Pending（这是真正"卡死"的情形）
            var stuckTasks = await taskRepo.GetByStatusAsync(BacktestTaskStatus.Running, ct);
            foreach (var task in stuckTasks)
            {
                logger.LogWarning("恢复卡死的回测任务: TaskId={TaskId}, Strategy={Strategy}", task.Id, task.StrategyName);
                task.Status = BacktestTaskStatus.Pending;
                await taskRepo.UpdateAsync(task, ct);
            }

            // 2) 把所有 Pending 任务全部入队（包括 Worker 离线期间累积的 + 刚回滚的）
            var pending = await taskRepo.GetByStatusAsync(BacktestTaskStatus.Pending, ct);
            foreach (var task in pending)
                await queue.EnqueueAsync(task.Id, ct);

            if (stuckTasks.Count > 0 || pending.Count > 0)
                logger.LogInformation("启动恢复完成: 卡死回滚 {Stuck} 个, Pending 入队 {Pending} 个",
                    stuckTasks.Count, pending.Count);
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

    // Adapter 内部已按各家 SDK 语义（Binance 升序、Bybit/OKX 降序、HTX 仅支持 last-N）
    // 翻页拉齐 [startAt, endAt] 区间数据。此处只做防御性 dedup/sort/clip。
    private static async Task<List<Candle>> FetchAllKlinesAsync(IExchangeClient client, string pair, string timeframe, DateTime startAt, DateTime endAt, CancellationToken ct)
    {
        var chunk = await client.GetKlinesAsync(pair, timeframe, startAt, endAt, ct);

        var result = chunk
            .Where(c => c.Timestamp >= startAt && c.Timestamp <= endAt)
            .GroupBy(c => c.Timestamp)
            .Select(g => g.First())
            .OrderBy(c => c.Timestamp)
            .ToList();

        if (result.Count == 0)
            throw new InvalidOperationException($"未获取到回测 K 线: Pair={pair}, Timeframe={timeframe}, Start={startAt:O}, End={endAt:O}");

        return result;
    }
}
