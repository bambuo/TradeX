using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Entities;

public class BacktestKlineAnalysisEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TaskId { get; init; }
    public int Index { get; set; }
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public string IndicatorsJson { get; set; } = "{}";
    public bool? EntryConditionResult { get; set; }
    public bool? ExitConditionResult { get; set; }
    public bool InPosition { get; set; }
    public string Action { get; set; } = "none";
    public decimal? AvgEntryPrice { get; set; }
    public decimal? PositionQuantity { get; set; }
    public decimal? PositionCost { get; set; }
    public decimal? PositionValue { get; set; }
    public decimal? PositionPnl { get; set; }
    public decimal? PositionPnlPercent { get; set; }

    public BacktestKlineAnalysis ToDomain() => new(
        Index, Timestamp, Open, High, Low, Close, Volume,
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(IndicatorsJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }) ?? [],
        EntryConditionResult, ExitConditionResult, InPosition, Action,
        AvgEntryPrice, PositionQuantity, PositionCost, PositionValue, PositionPnl, PositionPnlPercent);

    public static BacktestKlineAnalysisEntity FromDomain(Guid taskId, BacktestKlineAnalysis a) => new()
    {
        Id = Guid.NewGuid(),
        TaskId = taskId,
        Index = a.Index,
        Timestamp = a.Timestamp,
        Open = a.Open,
        High = a.High,
        Low = a.Low,
        Close = a.Close,
        Volume = a.Volume,
        IndicatorsJson = System.Text.Json.JsonSerializer.Serialize(a.IndicatorValues,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }),
        EntryConditionResult = a.EntryConditionResult,
        ExitConditionResult = a.ExitConditionResult,
        InPosition = a.InPosition,
        Action = a.Action,
        AvgEntryPrice = a.AvgEntryPrice,
        PositionQuantity = a.PositionQuantity,
        PositionCost = a.PositionCost,
        PositionValue = a.PositionValue,
        PositionPnl = a.PositionPnl,
        PositionPnlPercent = a.PositionPnlPercent
    };
}
