using TradeX.Core.Models;

namespace TradeX.Trading;

public record BacktestTrade(
    int EntryIndex,
    int ExitIndex,
    DateTime EntryTime,
    DateTime ExitTime,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal Quantity,
    decimal Pnl,
    decimal PnlPercent);

public interface IBacktestService
{
    Task<BacktestTask> StartBacktestAsync(Guid strategyId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
    Task<BacktestTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<BacktestResult?> GetResultAsync(Guid taskId, CancellationToken ct = default);
    Task<List<BacktestTask>> GetTasksByStrategyAsync(Guid strategyId, CancellationToken ct = default);
}
