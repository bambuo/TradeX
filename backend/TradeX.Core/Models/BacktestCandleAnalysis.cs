using System.Text.Json.Serialization;

namespace TradeX.Core.Models;

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
