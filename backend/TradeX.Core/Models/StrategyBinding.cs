using TradeX.Core.Enums;

namespace TradeX.Core.Models;

public class StrategyBinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StrategyId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid TraderId { get; init; }
    public Guid ExchangeId { get; set; }
    public string Pairs { get; set; } = "[]";
    public string Timeframe { get; set; } = "15m";
    public BindingStatus Status { get; set; } = BindingStatus.Disabled;
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
