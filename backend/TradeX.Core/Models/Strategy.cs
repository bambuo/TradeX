using System.Text.Json;
using TradeX.Core.Abstractions;
using TradeX.Core.Enums;
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

    /// <summary>策略运行模式：RuleChain。</summary>
    public StrategyMode Mode { get; set; } = StrategyMode.RuleChain;

    /// <summary>规则链定义 JSON（Mode=RuleChain 时使用，对应 Rules.ChainDefinition 数组）。</summary>
    public JsonElement Chains { get; set; } = default;

    /// <summary>节点参数 schema 版本号，用于向前兼容迁移（C2）。</summary>
    public int SchemaVersion { get; set; } = 1;

    public int Version { get; set; } = 1;
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ─────────────── 静态工厂方法 ───────────────

    public static Strategy Create(string name, Guid createdBy)
    {
        return new Strategy(name, createdBy);
    }

    public static Strategy CreateRuleChain(string name, JsonElement chains, Guid createdBy)
    {
        return new Strategy(name, createdBy)
        {
            Mode = StrategyMode.RuleChain,
            Chains = chains,
        };
    }

    // ─────────────── 领域方法 ───────────────

    public void UpdateRuleChain(JsonElement chains)
    {
        Mode = StrategyMode.RuleChain;
        Chains = chains;
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

    /// <summary>校验策略必填字段。</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Strategy name cannot be empty");

        return errors;
    }
}
