namespace TradeX.Core.Events;

// ─────────────── Order 领域事件 ───────────────

public sealed record OrderPlacedEvent(
    Guid OrderId, Guid TraderId, Guid ExchangeId, Guid? StrategyId,
    string Pair, string Side, string Type, decimal Quantity,
    decimal? Price) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record OrderFilledEvent(
    Guid OrderId, Guid TraderId, string Pair,
    string Side, decimal FilledQuantity, decimal Fee,
    string? FeeAsset) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record OrderCancelledEvent(
    Guid OrderId, Guid TraderId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record OrderFailedEvent(
    Guid OrderId, Guid TraderId, string Reason) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Position 领域事件 ───────────────

public sealed record PositionOpenedEvent(
    Guid PositionId, Guid TraderId, Guid StrategyId,
    string Pair, decimal Quantity, decimal EntryPrice) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record PositionClosedEvent(
    Guid PositionId, Guid TraderId, string Pair,
    decimal Quantity, decimal EntryPrice, decimal ExitPrice,
    decimal RealizedPnl) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── User 领域事件 ───────────────

public sealed record MfaEnabledEvent(Guid UserId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record UserLoggedInEvent(Guid UserId, DateTime LoginTime) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Trader 领域事件 ───────────────

public sealed record TraderStatusChangedEvent(
    Guid TraderId, Guid UserId, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── BacktestTask 领域事件 ───────────────

public sealed record BacktestStartedEvent(Guid TaskId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record BacktestCompletedEvent(
    Guid TaskId, decimal FinalValue, decimal TotalReturnPercent) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record BacktestFailedEvent(
    Guid TaskId, string Reason) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record BacktestCancelledEvent(
    Guid TaskId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── StrategyBinding 领域事件 ───────────────

public sealed record BindingStatusChangedEvent(
    Guid BindingId, Guid TraderId, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Strategy 领域事件 ───────────────

public sealed record StrategyConditionsUpdatedEvent(
    Guid StrategyId, string EntryCondition, string ExitCondition) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record StrategyVersionCreatedEvent(
    Guid StrategyId, int NewVersion) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── Exchange 领域事件 ───────────────

public sealed record ExchangeConnectionChangedEvent(
    Guid ExchangeId, Guid? TraderId, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─────────────── NotificationChannel 领域事件 ───────────────

public sealed record NotificationChannelStatusChangedEvent(
    Guid ChannelId, string Name, string OldStatus, string NewStatus) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
