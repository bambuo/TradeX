using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Core.Models;

public class Position : IVersioned
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TraderId { get; init; }
    public Guid ExchangeId { get; init; }
    public Guid StrategyId { get; init; }
    public string Pair { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public PositionStatus Status { get; set; } = PositionStatus.Open;
    public DateTime OpenedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>乐观并发控制版本号，由 VersionInterceptor 在保存前自动刷新。</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}
