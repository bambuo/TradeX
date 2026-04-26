namespace TradeX.Core.Models;

public class Strategy
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string EntryConditionJson { get; set; } = "{}";
    public string ExitConditionJson { get; set; } = "{}";
    public string ExecutionRuleJson { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
