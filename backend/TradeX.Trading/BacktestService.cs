using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class BacktestService(
    IBacktestTaskRepository taskRepo,
    IStrategyRepository strategyRepo,
    IExchangeAccountRepository accountRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryptionService,
    BacktestEngine engine,
    ILogger<BacktestService> logger) : IBacktestService
{
    public async Task<BacktestTask> StartBacktestAsync(Guid strategyId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        var strategy = await strategyRepo.GetByIdAsync(strategyId, ct);
        if (strategy is null)
            throw new ArgumentException($"策略不存在: {strategyId}");

        if (string.IsNullOrWhiteSpace(strategy.EntryConditionJson) || strategy.EntryConditionJson == "{}")
            throw new ArgumentException("策略缺少入场条件");

        var task = new BacktestTask
        {
            Id = Guid.NewGuid(),
            StrategyId = strategyId,
            Status = BacktestTaskStatus.Pending,
            StartAtUtc = startUtc,
            EndAtUtc = endUtc,
            CreatedBy = strategy.CreatedBy
        };

        await taskRepo.AddAsync(task, ct);

        try
        {
            task.Status = BacktestTaskStatus.Running;
            await taskRepo.UpdateAsync(task, ct);

            var account = await accountRepo.GetByIdAsync(strategy.ExchangeId, ct);
            if (account is null)
                throw new InvalidOperationException($"交易所账户不存在: {strategy.ExchangeId}");

            var apiKey = encryptionService.Decrypt(account.ApiKeyEncrypted);
            var secretKey = encryptionService.Decrypt(account.SecretKeyEncrypted);
            var client = clientFactory.CreateClient(account.Type, apiKey, secretKey);

            var symbolId = strategy.SymbolIds.Split(',', StringSplitOptions.RemoveEmptyEntries)[0];
            var klines = await client.GetKlinesAsync(symbolId, strategy.Timeframe, startUtc, endUtc, ct);
            var candles = klines.Select(k => new Candle(k.Timestamp, k.Open, k.High, k.Low, k.Close, k.Volume)).ToList();

            var (result, trades) = engine.Run(strategy, candles);

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
                DetailJson = result.DetailJson
            };
            await taskRepo.AddResultAsync(resultWithTask, ct);

            task.Status = BacktestTaskStatus.Completed;
            task.CompletedAtUtc = DateTime.UtcNow;
            await taskRepo.UpdateAsync(task, ct);

            logger.LogInformation("回测完成: StrategyId={StrategyId}, TaskId={TaskId}, Trades={TradeCount}, Return={Return}%",
                strategyId, task.Id, result.TotalTrades, result.TotalReturnPercent);
        }
        catch (Exception ex)
        {
            task.Status = BacktestTaskStatus.Failed;
            task.CompletedAtUtc = DateTime.UtcNow;
            await taskRepo.UpdateAsync(task, ct);

            logger.LogError(ex, "回测失败: StrategyId={StrategyId}, TaskId={TaskId}", strategyId, task.Id);
        }

        return task;
    }

    public async Task<BacktestTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default)
        => await taskRepo.GetByIdAsync(taskId, ct);

    public async Task<BacktestResult?> GetResultAsync(Guid taskId, CancellationToken ct = default)
        => await taskRepo.GetResultByTaskIdAsync(taskId, ct);

    public async Task<List<BacktestTask>> GetTasksByStrategyAsync(Guid strategyId, CancellationToken ct = default)
        => await taskRepo.GetByStrategyIdAsync(strategyId, ct);
}
