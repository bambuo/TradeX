using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>
/// 执行单条规则链。按 Phase→Priority 顺序执行节点，recover() 保护防止 panic 传播。
/// </summary>
public sealed class ChainEngine
{
    private readonly ChainDefinition _definition;
    private readonly IRuleNode[] _nodes;

    public ChainEngine(ChainDefinition definition, NodeRegistry registry)
    {
        _definition = definition;
        _nodes = BuildNodes(definition, registry);
    }

    public ChainDefinition Definition => _definition;

    private static IRuleNode[] BuildNodes(ChainDefinition def, NodeRegistry registry)
    {
        var items = new List<(IRuleNode Node, int Priority)>();
        foreach (var ni in def.Nodes)
        {
            var node = registry.CreateNode(ni.NodeKind, ni.Params);
            items.Add((node, ni.Priority));
        }

        // 按 (Phase, Priority) 升序排序（稳定排序保证同优先级保持定义顺序）
        items.Sort((a, b) =>
        {
            var cmp = a.Node.Phase.CompareTo(b.Node.Phase);
            return cmp != 0 ? cmp : a.Priority.CompareTo(b.Priority);
        });

        return [.. items.Select(i => i.Node)];
    }

    /// <summary>
    /// 执行规则链，修改 state。
    /// </summary>
    public async Task ExecuteAsync(ChainState state, CancellationToken ct = default)
    {
        for (var i = 0; i < _nodes.Length; i++)
        {
            if (state.Blocked || state.Terminated)
                return;

            var node = _nodes[i];

            try
            {
                await node.ProcessAsync(state, ct);
            }
            catch (Exception ex)
            {
                state.Errors.Add(new NodeError
                {
                    NodeKind = node.Kind,
                    Phase = node.Phase,
                    Message = $"panic recovered: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                });

                // 按阶段错误策略处理
                switch (node.Phase)
                {
                    case RulePhase.Gate:
                        state.Blocked = true;
                        return;

                    case RulePhase.Filter:
                    case RulePhase.Derive:
                    case RulePhase.Size:
                        // 非致命 → 跳过该节点输出，继续执行
                        continue;

                    case RulePhase.Action:
                        state.Terminated = true;
                        return;

                    case RulePhase.Risk:
                        // 风控错误 → 保守拒绝所有 Action，继续让其余风控/Override 执行
                        state.Actions = [];
                        continue;

                    case RulePhase.Override:
                        state.Actions = [];
                        state.Terminated = true;
                        return;
                }
            }
        }
    }
}
