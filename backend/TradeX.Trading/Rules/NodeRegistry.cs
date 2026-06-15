using System.Collections.Concurrent;
using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>规则链引擎的共享 JSON 序列化选项——使用小驼峰命名、大小写不敏感。</summary>
public static class RuleJsonOptions
{
    /// <summary>
    /// 使用 Web 默认策略：PropertyNamingPolicy = CamelCase, PropertyNameCaseInsensitive = true。
    /// 确保前端小驼峰 JSON（nodeKind, schemaVersion）能反序列化为 C# PascalCase 属性。
    /// </summary>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}

/// <summary>节点工厂函数，根据 JSON 参数创建 RuleNode 实例。</summary>
public delegate IRuleNode NodeFactory(JsonElement @params);

/// <summary>节点描述符元信息。</summary>
public sealed class NodeDescriptor
{
    public string Kind { get; set; } = string.Empty;
    public RulePhase Phase { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool AllowDuplicate { get; set; }
    public bool ProducesDecisions { get; set; }
    public List<string> EmitNames { get; set; } = [];
    public string? EmitScope { get; set; }
    public List<ParamDescriptor> Params { get; set; } = [];
    public List<object> Examples { get; set; } = [];
}

/// <summary>参数描述符元信息。</summary>
public sealed class ParamDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string"; // "string" / "float" / "int" / "bool" / "string[]" / "ref"
    public bool Required { get; set; }
    public object? Default { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public List<string>? Enum { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? RefScope { get; set; }
    public string? Unit { get; set; }
}

/// <summary>节点注册表，管理所有可用节点类型的注册和查找。线程安全。</summary>
public sealed class NodeRegistry
{
    private readonly ConcurrentDictionary<string, (NodeDescriptor Desc, NodeFactory Factory)> _entries = new();

    /// <summary>注册一个节点类型。</summary>
    public void Register(string kind, NodeDescriptor desc, NodeFactory factory)
    {
        if (!_entries.TryAdd(kind, (desc, factory)))
            throw new InvalidOperationException($"Node kind '{kind}' already registered");
    }

    /// <summary>简单注册（无描述符元信息）。</summary>
    public void Register(string kind, NodeFactory factory)
    {
        Register(kind, new NodeDescriptor { Kind = kind }, factory);
    }

    /// <summary>查找已注册的节点描述和工厂。</summary>
    public (NodeDescriptor? Desc, NodeFactory? Factory) Lookup(string kind)
    {
        if (_entries.TryGetValue(kind, out var entry))
            return (entry.Desc, entry.Factory);
        return (null, null);
    }

    /// <summary>检查指定 Kind 是否已注册。</summary>
    public bool Has(string kind) => _entries.ContainsKey(kind);

    /// <summary>返回所有已注册节点的描述列表。</summary>
    public List<NodeDescriptor> ListAll() =>
        [.. _entries.Values.Select(e => e.Desc)];

    /// <summary>按分类返回节点描述列表。</summary>
    public List<NodeDescriptor> ListByCategory(string category) =>
        _entries.Values.Where(e => e.Desc.Category == category).Select(e => e.Desc).ToList();

    /// <summary>根据 Kind 和 JSON 参数创建节点实例。</summary>
    public IRuleNode CreateNode(string kind, JsonElement @params)
    {
        if (!_entries.TryGetValue(kind, out var entry))
            throw new InvalidOperationException($"Unknown node kind: '{kind}'");
        return entry.Factory(@params);
    }
}
