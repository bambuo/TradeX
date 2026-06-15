using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// Filter 阶段节点 — 信号过滤/转换
// ═══════════════════════════════════════════════════════════════════

// ── min_notional ──
internal sealed record MinNotionalParams(decimal MinNotional);

internal sealed class MinNotionalNode(JsonElement @params) : IRuleNode
{
    public string Kind => "min_notional";
    public RulePhase Phase => RulePhase.Filter;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MinNotionalParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.MinNotional <= decimal.Zero) return Task.CompletedTask;

        for (var i = state.Actions.Count - 1; i >= 0; i--)
        {
            var action = state.Actions[i];
            if (!string.Equals(action.Intent, "BUY", StringComparison.OrdinalIgnoreCase))
                continue;

            var notional = action.Quantity * state.Context.CurrentPrice;
            if (notional < p.MinNotional)
                state.Actions.RemoveAt(i);
        }

        return Task.CompletedTask;
    }
}

// ── max_slippage ──
internal sealed record MaxSlippageParams(decimal MaxSlippagePercent);

internal sealed class MaxSlippageNode(JsonElement @params) : IRuleNode
{
    public string Kind => "max_slippage";
    public RulePhase Phase => RulePhase.Filter;
    public IReadOnlyList<string> Deps => ["ESTIMATED_SLIPPAGE"];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MaxSlippageParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        if (!state.Signals.TryGetValue("ESTIMATED_SLIPPAGE", out var sig))
            return Task.CompletedTask;

        if (sig.Value > p.MaxSlippagePercent)
            state.Blocked = true;

        return Task.CompletedTask;
    }
}

// ── liquidity_filter ──
internal sealed record LiquidityFilterParams(string Side, decimal MinDepth); // Side: "BUY" / "SELL"

internal sealed class LiquidityFilterNode(JsonElement @params) : IRuleNode
{
    public string Kind => "liquidity_filter";
    public RulePhase Phase => RulePhase.Filter;
    public IReadOnlyList<string> Deps => ["BID_AGGREGATE_DEPTH", "ASK_AGGREGATE_DEPTH"];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<LiquidityFilterParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.MinDepth <= decimal.Zero) return Task.CompletedTask;

        var isBuy = string.Equals(p.Side, "BUY", StringComparison.OrdinalIgnoreCase);
        var signalKey = isBuy ? "BID_AGGREGATE_DEPTH" : "ASK_AGGREGATE_DEPTH";

        if (!state.Signals.TryGetValue(signalKey, out var depthSig))
            return Task.CompletedTask;

        // 深度按名义价值计算：depthValue * CurrentPrice
        var depthNotional = depthSig.Value * state.Context.CurrentPrice;
        if (depthNotional < p.MinDepth)
            state.Blocked = true;

        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class FilterNodesRegistration
{
    public static void RegisterFilterNodes(this NodeRegistry reg)
    {
        reg.Register("min_notional", new NodeDescriptor
        {
            Kind = "min_notional", Phase = RulePhase.Filter,
            Description = "最小名义过滤：低于最小名义值的买单被移除",
            Category = "Filter",
            Params = [
                new() { Name = "minNotional", Type = "float", Required = true,
                    Min = 0, Description = "最小名义金额", Unit = "USDT" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "最少 10 USDT", ["params"] = new Dictionary<string, object> { ["minNotional"] = 10m } }
            ]
        }, p => new MinNotionalNode(p));

        reg.Register("max_slippage", new NodeDescriptor
        {
            Kind = "max_slippage", Phase = RulePhase.Filter,
            Description = "滑点过滤：估算滑点超过阈值时阻断",
            Category = "Filter",
            Params = [
                new() { Name = "maxSlippagePercent", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "最大允许滑点", Unit = "%" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "滑点不超过 0.5%", ["params"] = new Dictionary<string, object> { ["maxSlippagePercent"] = 0.5m } }
            ]
        }, p => new MaxSlippageNode(p));

        reg.Register("liquidity_filter", new NodeDescriptor
        {
            Kind = "liquidity_filter", Phase = RulePhase.Filter,
            Description = "流动性过滤：订单簿深度不足时阻断",
            Category = "Filter",
            Params = [
                new() { Name = "side", Type = "string", Required = true,
                    Enum = ["BUY", "SELL"], Description = "检查哪侧的深度" },
                new() { Name = "minDepth", Type = "float", Required = true,
                    Min = 0, Description = "最小深度名义值", Unit = "USDT" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "买单深度至少 5000 USDT", ["params"] = new Dictionary<string, object> { ["side"] = "BUY", ["minDepth"] = 5000m } }
            ]
        }, p => new LiquidityFilterNode(p));
    }
}
