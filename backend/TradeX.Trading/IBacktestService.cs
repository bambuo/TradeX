using TradeX.Core.Models;

namespace TradeX.Trading;

public interface IBacktestService
{
    Task<BacktestTask> StartBacktestAsync(Guid strategyId, Guid exchangeId, string pair, string timeframe, DateTime startAt, DateTime endAt, decimal initialCapital, decimal? positionSize = null, CancellationToken ct = default);
    Task<BacktestTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<BacktestResult?> GetResultAsync(Guid taskId, CancellationToken ct = default);
    Task<List<BacktestTask>> GetTasksByStrategyAsync(Guid strategyId, CancellationToken ct = default);
}
