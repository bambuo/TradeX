using TradeX.Core.Abstractions;
using TradeX.Core.Events;

namespace TradeX.Core.Models;

public class Strategy : AggregateRoot
{
    // EF Core 无参构造函数
    public Strategy() { }

    private Strategy(string name, Guid createdBy)
    {
        Name = name;
        CreatedBy = createdBy;
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string EntryCondition { get; set; } = "{}";
    public string ExitCondition { get; set; } = "{}";
    public string ExecutionRule { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ─────────────── 静态工厂方法 ───────────────

    public static Strategy Create(string name, Guid createdBy)
    {
        return new Strategy(name, createdBy);
    }

    public static Strategy CreateWithConditions(string name, string entryCondition, string exitCondition, Guid createdBy)
    {
        return new Strategy(name, createdBy)
        {
            EntryCondition = entryCondition,
            ExitCondition = exitCondition,
        };
    }

    // ─────────────── 领域方法 ───────────────

    public void UpdateConditions(string entry, string exit)
    {
        EntryCondition = entry;
        ExitCondition = exit;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new StrategyConditionsUpdatedEvent(Id, entry, exit));
    }

    public void UpdateExecutionRule(string rule)
    {
        ExecutionRule = rule;
        UpdatedAt = DateTime.UtcNow;
    }

    public void NewVersion()
    {
        Version++;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new StrategyVersionCreatedEvent(Id, Version));
    }

    public void Rename(string name)
    {
        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }
}
