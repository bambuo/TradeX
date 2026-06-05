namespace TradeX.Core.Events;

// ─────────────── Order 领域事件 ───────────────

public sealed record OrderPlacedDomainEvent(
    Guid OrderId, Guid TraderId, Guid ExchangeId, Guid? StrategyId,
    string Pair, string Side, string Type, decimal Quantity,
    decimal? Price) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record OrderFilledDomainEvent(
    Guid OrderId, Guid TraderId, string Pair,
    string Side, decimal FilledQuantity, decimal Fee,
    string? FeeAsset) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record OrderCancelledDomainEvent(
    Guid OrderId, Guid TraderId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record OrderFailedDomainEvent(
    Guid OrderId, Guid TraderId, string Reason) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Position 领域事件 ───────────────

public sealed record PositionOpenedDomainEvent(
    Guid PositionId, Guid TraderId, Guid StrategyId,
    string Pair, decimal Quantity, decimal EntryPrice) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record PositionClosedDomainEvent(
    Guid PositionId, Guid TraderId, string Pair,
    decimal Quantity, decimal EntryPrice, decimal ExitPrice,
    decimal RealizedPnl) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── User 领域事件 ───────────────

public sealed record MfaEnabledDomainEvent(Guid UserId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record UserLoggedInDomainEvent(Guid UserId, DateTime LoginTime) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Trader 领域事件 ───────────────

public sealed record TraderStatusChangedDomainEvent(
    Guid TraderId, Guid UserId, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── BacktestTask 领域事件 ───────────────

public sealed record BacktestStartedDomainEvent(Guid TaskId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record BacktestCompletedDomainEvent(
    Guid TaskId, decimal FinalValue, decimal TotalReturnPercent) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record BacktestFailedDomainEvent(
    Guid TaskId, string Reason) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record BacktestCancelledDomainEvent(
    Guid TaskId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── StrategyBinding 领域事件 ───────────────

public sealed record BindingStatusChangedDomainEvent(
    Guid BindingId, Guid TraderId, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Strategy 领域事件 ───────────────

public sealed record StrategyConditionsUpdatedDomainEvent(
    Guid StrategyId, string EntryCondition, string ExitCondition) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record StrategyVersionCreatedDomainEvent(
    Guid StrategyId, int NewVersion) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Exchange 领域事件 ───────────────

public sealed record ExchangeConnectionChangedDomainEvent(
    Guid ExchangeId, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── NotificationChannel 领域事件 ───────────────

public sealed record NotificationChannelStatusChangedDomainEvent(
    Guid ChannelId, string Name, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
