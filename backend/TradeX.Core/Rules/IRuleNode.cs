using TradeX.Core.Enums;

namespace TradeX.Core.Rules;

/// <summary>规则链中的最小处理单元接口。</summary>
public interface IRuleNode
{
    /// <summary>返回此节点类型的唯一标识（如 "regime_gate"、"signal_action"）。</summary>
    string Kind { get; }

    /// <summary>返回节点所属的执行阶段。</summary>
    RulePhase Phase { get; }

    /// <summary>声明此节点需要消费的信号名称列表。</summary>
    IReadOnlyList<string> Deps { get; }

    /// <summary>执行节点逻辑，修改 chainState。返回错误时由 ChainEngine 按阶段错误策略处理。</summary>
    Task ProcessAsync(ChainState state, CancellationToken ct);
}
