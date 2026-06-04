namespace TradeX.Trading.Events;

/// <summary>
/// Redis Pub/Sub 跨进程事件包络。Worker 发布，API bridge 订阅后转发到 SignalR。
/// <c>DataJson</c> 由发布方序列化为 JSON 字符串，订阅方按 <c>Type</c> 决定解析成哪个具体事件记录。
/// </summary>
public sealed record TradingEventEnvelope(
    string Type,
    Guid TraceId,
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
    /// <summary>系统级告警：对账发现交易所有、本地无记录的孤儿订单。无 trader 归属，推送至管理员组。</summary>
    public const string OrphanOrderDetected = "OrphanOrderDetected";
    /// <summary>系统级告警：持仓级对账发现本地开仓量与交易所余额漂移超阈值。推送至管理员组。</summary>
    public const string PositionDriftDetected = "PositionDriftDetected";
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

public sealed record OrphanOrderDetectedPayload(
    Guid ExchangeId, string ExchangeType, string Pair, string ExchangeOrderId,
    string Side, string Type, decimal Price, decimal Quantity, DateTime DetectedAt);

/// <summary>
/// 持仓漂移告警载荷。<c>Drift = LocalQuantity - ExchangeQuantity</c>：
/// 正值=本地多于实际（高危，可能卖空头寸），负值=交易所盈余（多为人工存入/未跟踪持仓）。
/// </summary>
public sealed record PositionDriftDetectedPayload(
    Guid ExchangeId, string ExchangeType, Guid? TraderId, string Asset,
    decimal LocalQuantity, decimal ExchangeQuantity, decimal Drift, decimal DriftPercent,
    string Severity, DateTime DetectedAt);
