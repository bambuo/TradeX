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

    public static Strategy CreateWithRule(string name, string executionRule, Guid createdBy)
    {
        return new Strategy(name, createdBy)
        {
            ExecutionRule = executionRule,
        };
    }

    // ─────────────── 领域方法 ───────────────

    public void UpdateExecutionRule(string rule)
    {
        ExecutionRule = rule;
        UpdatedAt = DateTime.UtcNow;
    }

    public void NewVersion()
    {
        Version++;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new StrategyVersionCreatedDomainEvent(Id, Version));
    }

    public void Rename(string name)
    {
        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }
}
