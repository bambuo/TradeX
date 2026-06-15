using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeX.Core.Rules;

/// <summary>规则链的静态定义（Strategy.Chains 的元素）。</summary>
public sealed class ChainDefinition
{
    /// <summary>链的唯一标识（同一 Strategy 内唯一）。</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>链的人类可读名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>链的描述（可选）。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>节点参数 schema 版本号，用于向前兼容迁移（C2）。</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    /// <summary>链内的节点实例列表，运行时按 (Phase, Priority) 升序执行。</summary>
    [JsonPropertyName("nodes")]
    public List<NodeInstance> Nodes { get; set; } = [];
}

/// <summary>链内的一个节点实例，引用已注册的节点类型并携带具体参数。</summary>
public sealed class NodeInstance
{
    /// <summary>引用的注册节点类型标识（如 "regime_gate"、"signal_action"）。</summary>
    [JsonPropertyName("nodeKind")]
    public string NodeKind { get; set; } = string.Empty;

    /// <summary>节点特定参数，JSON 格式。</summary>
    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }

    /// <summary>
    /// 同阶段（Phase）内排序权重，数值越小优先级越高（R-L1）。
    /// 规范形式下 Nodes 数组已按 (Phase, Priority) 升序排列。
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}
