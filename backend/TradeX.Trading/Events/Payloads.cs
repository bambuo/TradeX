namespace TradeX.Trading.Events;
public sealed record PositionUpdatedPayload(
    Guid PositionId, Guid TraderId, Guid ExchangeId, Guid StrategyId,
    string Pair, decimal Quantity, decimal EntryPrice, decimal UnrealizedPnl,
    decimal RealizedPnl, string Status, DateTime UpdatedAt);

public sealed record OrderPlacedPayload(
    Guid OrderId, Guid TraderId, Guid ExchangeId, Guid? StrategyId,
    string Pair, string Side, string Type, string Status,
    decimal Quantity, DateTime PlacedAt);

public sealed record BindingStatusChangedPayload(
    Guid StrategyId, Guid TraderId, string OldStatus, string NewStatus,
    string? Reason, DateTime ChangedAt);

public sealed record RiskAlertPayload(
    Guid AlertId, string Level, string Category, Guid TraderId,
    Guid? StrategyId, string Message, DateTime TriggeredAt);

public sealed record DashboardSummaryPayload(
    decimal TotalPnl, int TotalPositions, int ActiveStrategies,
    decimal DailyPnl, decimal WinRate, DateTime LastUpdatedAt);

public sealed record ExchangeConnectionChangedPayload(
    Guid ExchangeId, Guid TraderId, string OldStatus,
    string NewStatus, string? ErrorMessage, DateTime ChangedAt);

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

public sealed record KillSwitchActivatedPayload(
    string Reason, Guid? ActorUserId, DateTime ActivatedAt, int DisabledBindingCount);

public sealed record KillSwitchDeactivatedPayload(
    string Reason, Guid? ActorUserId, DateTime DeactivatedAt);
