namespace TradeX.Trading.Events;

/// <summary>
/// Redis Pub/Sub 跨进程事件包络。Worker 发布，API bridge 订阅后转发到 SignalR。
/// <c>DataJson</c> 由发布方序列化为 JSON 字符串，订阅方按 <c>Type</c> 决定解析成哪个具体事件记录。
/// </summary>
public sealed record TradingEventEnvelope(
    string Type,
    Guid TraderId,
    string DataJson);

public static class TradingEventTypes
{
    public const string PositionUpdated = "PositionUpdated";
    public const string OrderPlaced = "OrderPlaced";
    public const string BindingStatusChanged = "BindingStatusChanged";
    public const string RiskAlert = "RiskAlert";
    public const string DashboardSummary = "DashboardSummary";
    public const string ExchangeConnectionChanged = "ExchangeConnectionChanged";
}

public static class TradingEventChannels
{
    /// <summary>所有业务事件汇入此 Redis 频道。</summary>
    public const string Events = "tradex:events";
}

// 与 API 端 TradingHub 中的事件记录字段保持一致，便于 bridge 反序列化后直接用。
public sealed record PositionUpdatedPayload(
    Guid PositionId, Guid TraderId, Guid ExchangeId, Guid StrategyId,
    string Pair, decimal Quantity, decimal EntryPrice, decimal UnrealizedPnl,
    decimal RealizedPnl, string Status, DateTime UpdatedAt);

public sealed record OrderPlacedPayload(
    Guid OrderId, Guid TraderId, Guid ExchangeId, Guid? StrategyId,
    string Pair, string Side, string Type, string Status,
    decimal Quantity, DateTime PlacedAtUtc);

public sealed record BindingStatusChangedPayload(
    Guid StrategyId, Guid TraderId, string OldStatus, string NewStatus,
    string? Reason, DateTime ChangedAtUtc);

public sealed record RiskAlertPayload(
    Guid AlertId, string Level, string Category, Guid TraderId,
    Guid? StrategyId, string Message, DateTime TriggeredAtUtc);

public sealed record DashboardSummaryPayload(
    decimal TotalPnl, int TotalPositions, int ActiveStrategies,
    decimal DailyPnl, decimal WinRate, DateTime LastUpdateAtUtc);

public sealed record ExchangeConnectionChangedPayload(
    Guid ExchangeId, Guid TraderId, string OldStatus,
    string NewStatus, string? ErrorMessage, DateTime ChangedAtUtc);
