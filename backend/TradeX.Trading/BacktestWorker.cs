using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;

namespace TradeX.Trading;

public class BacktestWorker(
    IServiceScopeFactory scopeFactory,
    IBacktestTaskQueue queue,
    ILogger<BacktestWorker> logger,
    TaskAnalysisStore analysisStore) : BackgroundService
{
    private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BacktestWorker 启动");

        await RecoverStuckTasksAsync(stoppingToken);

        await foreach (var taskId in queue.ReadAllAsync(stoppingToken))
        {
            logger.LogInformation("BacktestWorker 开始处理任务: TaskId={TaskId}", taskId);
            await ProcessTaskWithTimeoutAsync(taskId, stoppingToken);
        }
    }

    private async Task RecoverStuckTasksAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();

            var strategyRepo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
            var deployments = await strategyRepo.GetAllAsync(ct);
            var stuckTasks = new List<BacktestTask>();

            foreach (var dep in deployments)
            {
                var tasks = await taskRepo.GetByStrategyIdAsync(dep.Id, ct);
                stuckTasks.AddRange(tasks.Where(t => t.Status == BacktestTaskStatus.Running));
            }

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

    private async Task ProcessTaskWithTimeoutAsync(Guid taskId, CancellationToken stoppingToken)
    {
        using var timeoutCts = new CancellationTokenSource(TaskTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
        var ct = linkedCts.Token;

        try
        {
            await ProcessTaskAsync(taskId, ct);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("回测任务超时: TaskId={TaskId}, Timeout={Timeout}", taskId, TaskTimeout);
            await FailTaskAsync(taskId, "回测执行超时");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("回测任务被取消: TaskId={TaskId}", taskId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "回测任务异常: TaskId={TaskId}", taskId);
            await FailTaskAsync(taskId, ex.Message);
        }
    }

    private async Task ProcessTaskAsync(Guid taskId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var strategyRepo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
        var accountRepo = scope.ServiceProvider.GetRequiredService<IExchangeAccountRepository>();
        var clientFactory = scope.ServiceProvider.GetRequiredService<IExchangeClientFactory>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var indicatorService = scope.ServiceProvider.GetRequiredService<IIndicatorService>();
        var conditionEvaluator = scope.ServiceProvider.GetRequiredService<IConditionEvaluator>();
        var iotdb = scope.ServiceProvider.GetRequiredService<IIoTDbService>();

        var task = await taskRepo.GetByIdAsync(taskId, ct);
        if (task is null)
        {
            logger.LogWarning("回测任务不存在: TaskId={TaskId}", taskId);
            return;
        }

        task.Status = BacktestTaskStatus.Running;
        task.Phase = BacktestPhase.Queued;
        await taskRepo.UpdateAsync(task, ct);

        var deploymentRepo = scope.ServiceProvider.GetRequiredService<IStrategyDeploymentRepository>();
        var deployment = task.DeploymentId != Guid.Empty
            ? await deploymentRepo.GetByIdAsync(task.DeploymentId, ct)
            : null;

        var strategy = await strategyRepo.GetByIdAsync(task.StrategyId, ct);
        if (strategy is null)
            throw new InvalidOperationException($"策略不存在: {task.StrategyId}");

        var account = await accountRepo.GetByIdAsync(task.ExchangeId, ct);
        if (account is null)
            throw new InvalidOperationException($"交易所账户不存在: {task.ExchangeId}");

        var exchangeName = account.Type.ToString();

        task.Phase = BacktestPhase.FetchingData;
        await taskRepo.UpdateAsync(task, ct);

        List<Candle> candles;
        var startUtc = task.StartAtUtc;
        var endUtc = task.EndAtUtc;
        var symbolId = task.SymbolId;
        var timeframe = task.Timeframe;
        var tolerance = TimeSpan.FromMilliseconds(GetIntervalMs(timeframe));
        var dataFromIotdb = false;

        var iotdbTask = iotdb.GetKlinesAsync(exchangeName, symbolId, timeframe, startUtc, endUtc, ct);
        var exchangeClient = clientFactory.CreateClient(
            account.Type,
            encryptionService.Decrypt(account.ApiKeyEncrypted),
            encryptionService.Decrypt(account.SecretKeyEncrypted));
        var exchangeTask = exchangeClient.GetKlinesAsync(symbolId, timeframe, startUtc, endUtc, ct);

        var completedTask = await Task.WhenAny(iotdbTask, exchangeTask);

        if (completedTask == iotdbTask)
        {
            var iotdbCandles = await iotdbTask;
            var iotdbValid = iotdbCandles.Length >= 2
                 && iotdbCandles[0].Timestamp <= startUtc + tolerance
                 && iotdbCandles[^1].Timestamp >= endUtc - tolerance;
            if (iotdbValid)
            {
                candles = iotdbCandles.ToList();
                dataFromIotdb = true;
                logger.LogInformation("从 IoTDB 缓存读取 K 线数据: {Count} 条", candles.Count);
            }
            else
            {
                candles = await FetchAllCandlesAsync(exchangeClient, symbolId, timeframe, startUtc, endUtc, ct);
            }
        }
        else
        {
            candles = await FetchAllCandlesAsync(exchangeClient, symbolId, timeframe, startUtc, endUtc, ct);
        }

        if (!dataFromIotdb)
            _ = iotdb.WriteKlinesAsync(exchangeName, symbolId, timeframe, candles, ct);

        task.Phase = BacktestPhase.Running;
        await taskRepo.UpdateAsync(task, ct);

        analysisStore.Init(task.Id);

        var engine = new BacktestEngine(indicatorService, conditionEvaluator);
        var (result, trades, analysis) = engine.Run(strategy, candles, task.InitialCapital,
            a => analysisStore.Push(task.Id, a));

        var resultWithTask = new BacktestResult
        {
            TaskId = task.Id,
            TotalReturnPercent = result.TotalReturnPercent,
            AnnualizedReturnPercent = result.AnnualizedReturnPercent,
            MaxDrawdownPercent = result.MaxDrawdownPercent,
            WinRate = result.WinRate,
            TotalTrades = result.TotalTrades,
            SharpeRatio = result.SharpeRatio,
            ProfitLossRatio = result.ProfitLossRatio,
            DetailJson = result.DetailJson,
            AnalysisJson = result.AnalysisJson
        };
        await taskRepo.AddResultAsync(resultWithTask, ct);

        task.Status = BacktestTaskStatus.Completed;
        task.Phase = null;
        task.CompletedAtUtc = DateTime.UtcNow;
        await taskRepo.UpdateAsync(task, ct);

        if (deployment?.Status == StrategyStatus.Draft)
        {
            deployment.Status = StrategyStatus.Passed;
            await deploymentRepo.UpdateAsync(deployment, ct);
        }

        analysisStore.Remove(task.Id);

        logger.LogInformation("回测完成: StrategyId={StrategyId}, TaskId={TaskId}, Trades={TradeCount}, Return={Return}%",
            task.StrategyId, task.Id, result.TotalTrades, result.TotalReturnPercent);
    }

    private async Task FailTaskAsync(Guid taskId, string reason)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var taskRepo = scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
            var task = await taskRepo.GetByIdAsync(taskId);
            if (task is not null)
            {
                task.Status = BacktestTaskStatus.Failed;
                task.CompletedAtUtc = DateTime.UtcNow;
                await taskRepo.UpdateAsync(task);
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

    private async Task<List<Candle>> FetchAllCandlesAsync(IExchangeClient client, string symbol, string timeframe, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        var allCandles = new List<Candle>();
        var currentStart = startUtc;
        var intervalMs = GetIntervalMs(timeframe);
        var totalExpected = (int)((endUtc - startUtc).TotalMilliseconds / intervalMs) + 1;

        while (currentStart < endUtc && !ct.IsCancellationRequested)
        {
            var chunk = await client.GetKlinesAsync(symbol, timeframe, currentStart, endUtc, ct);
            if (chunk.Length == 0) break;

            var converted = chunk.Select(k => new Candle(k.Timestamp, k.Open, k.High, k.Low, k.Close, k.Volume)).ToList();

            var newCandles = converted
                .Where(c => c.Timestamp >= currentStart && !allCandles.Any(ex => ex.Timestamp == c.Timestamp))
                .ToList();

            if (newCandles.Count == 0) break;

            allCandles.AddRange(newCandles);

            var lastTime = newCandles[^1].Timestamp;
            if (lastTime >= endUtc - TimeSpan.FromMilliseconds(intervalMs)) break;

            currentStart = lastTime.AddMilliseconds(intervalMs);
        }

        logger.LogInformation("从交易所 API 拉取 K 线数据: {Count} 条 (期望 {Expected} 条)", allCandles.Count, totalExpected);
        return allCandles;
    }
}
