using TradeX.Core.Models;

namespace TradeX.Trading;

public interface IBacktestService
{
    Task<BacktestTask> StartBacktestAsync(Guid deploymentId, Guid strategyId, Guid exchangeId, string symbolId, string timeframe, DateTime startUtc, DateTime endUtc, decimal initialCapital = 1000m, CancellationToken ct = default);
    Task<BacktestTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<BacktestResult?> GetResultAsync(Guid taskId, CancellationToken ct = default);
    Task<List<BacktestTask>> GetTasksByStrategyAsync(Guid strategyId, CancellationToken ct = default);
}
