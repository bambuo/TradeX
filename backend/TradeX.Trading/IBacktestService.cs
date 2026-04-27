using System.Text.Json.Serialization;
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

public record BacktestCandleAnalysis(
    int Index,
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    [property: JsonPropertyName("indicators")] Dictionary<string, decimal> IndicatorValues,
    [property: JsonPropertyName("entry")] bool? EntryConditionResult,
    [property: JsonPropertyName("exit")] bool? ExitConditionResult,
    bool InPosition,
    string Action,
    decimal? AvgEntryPrice = null,
    decimal? PositionQuantity = null,
    decimal? PositionCost = null,
    decimal? PositionValue = null,
    decimal? PositionPnl = null,
    decimal? PositionPnlPercent = null);

public interface IBacktestService
{
    Task<BacktestTask> StartBacktestAsync(Guid deploymentId, Guid strategyId, Guid exchangeId, string symbolId, string timeframe, DateTime startUtc, DateTime endUtc, decimal initialCapital = 1000m, CancellationToken ct = default);
    Task<BacktestTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<BacktestResult?> GetResultAsync(Guid taskId, CancellationToken ct = default);
    Task<List<BacktestTask>> GetTasksByStrategyAsync(Guid strategyId, CancellationToken ct = default);
}
