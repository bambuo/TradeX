using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// cost_anchored_rebalance — 基于成本锚点的再平衡节点（Derive 阶段，产出 ActionDecision）
// ═══════════════════════════════════════════════════════════════════

internal sealed record CostAnchoredRebalanceParams(decimal DeviationThreshold, decimal BaseQuantity);

internal sealed class CostAnchoredRebalanceNode(JsonElement @params) : IRuleNode
{
    public string Kind => "cost_anchored_rebalance";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps => [];

    public async Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<CostAnchoredRebalanceParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.BaseQuantity <= decimal.Zero) return;

        var currentPrice = state.Context.CurrentPrice;
        var pos = state.Context.Position;

        // 从 StateStore 读取基准成本价（anchoredCost）
        var store = state.Context.StateStore;
        var anchoredCost = pos?.EntryPrice ?? decimal.Zero;

        if (store is not null)
        {
            var nodeState = await store.ReadStateAsync(state.Context.ScopeKey, Kind, ct);
            if (nodeState?.Data.TryGetValue("anchoredCost", out var acEl) == true)
                anchoredCost = acEl.GetDecimal();
        }

        // 没有锚定成本，无法判断偏离
        if (anchoredCost <= decimal.Zero)
        {
            // 首次使用：以当前持仓均价为锚
            if (pos?.EntryPrice > decimal.Zero)
            {
                anchoredCost = pos.EntryPrice;
                // 写回StateStore避免锚点漂移
                if (store is not null)
                {
                    var initState = new NodeState
                    {
                        Data = new Dictionary<string, JsonElement>
                        {
                            ["anchoredCost"] = JsonSerializer.SerializeToElement(anchoredCost)
                        }
                    };
                    await store.WriteStateAsync(state.Context.ScopeKey, Kind, initState, ct);
                }
            }
            else
                return;
        }

        // 计算偏离百分比
        var deviation = (currentPrice - anchoredCost) / anchoredCost * 100m;

        if (Math.Abs(deviation) < p.DeviationThreshold)
            return;

        // 偏离超过阈值 → 反向再平衡
        if (deviation > 0)
        {
            // 价格上涨偏离过大 → 卖出
            state.Actions.Add(new ActionDecision
            {
                Intent = "SELL",
                Quantity = p.BaseQuantity,
                OrderType = "MARKET",
                Priority = 8,
                Pair = state.Context.Pair,
                Reason = $"cost_anchored_rebalance:SELL:deviation={deviation:F2}%"
            });
        }
        else
        {
            // 价格下跌偏离过大 → 买入
            state.Actions.Add(new ActionDecision
            {
                Intent = "BUY",
                Quantity = p.BaseQuantity,
                OrderType = "MARKET",
                Priority = 8,
                Pair = state.Context.Pair,
                Reason = $"cost_anchored_rebalance:BUY:deviation={deviation:F2}%"
            });
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class CostAnchorNodeRegistration
{
    public static void RegisterCostAnchorNode(this NodeRegistry reg)
    {
        reg.Register("cost_anchored_rebalance", new NodeDescriptor
        {
            Kind = "cost_anchored_rebalance", Phase = RulePhase.Derive,
            Description = "成本锚点再平衡：价格偏离持仓成本超过阈值时反向再平衡",
            Category = "Derive", ProducesDecisions = true,
            Params = [
                new() { Name = "deviationThreshold", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "偏离阈值", Unit = "%" },
                new() { Name = "baseQuantity", Type = "float", Required = true,
                    Min = 0, Description = "再平衡数量" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "5% 偏离再平衡 0.01", ["params"] = new Dictionary<string, object> { ["deviationThreshold"] = 5m, ["baseQuantity"] = 0.01m } }
            ]
        }, p => new CostAnchoredRebalanceNode(p));
    }
}
