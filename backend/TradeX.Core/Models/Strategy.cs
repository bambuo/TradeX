using TradeX.Core.Enums;

namespace TradeX.Core.Models;

public class Strategy
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TraderId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid ExchangeId { get; set; }
    public string SymbolIds { get; set; } = "[]";
    public string Timeframe { get; set; } = "15m";
    public string EntryConditionJson { get; set; } = "{}";
    public string ExitConditionJson { get; set; } = "{}";
    public string ExecutionRuleJson { get; set; } = "{}";
    public StrategyStatus Status { get; set; } = StrategyStatus.Draft;
    public int Version { get; set; } = 1;
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
