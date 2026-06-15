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
                anchoredCost = pos.EntryPrice;
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
        reg.Register("cost_anchored_rebalance", p => new CostAnchoredRebalanceNode(p));
    }
}
