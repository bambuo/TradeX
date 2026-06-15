using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// Risk 阶段节点 — 风控检查，过滤/缩放 Actions
// ═══════════════════════════════════════════════════════════════════

// ── max_position_size ──
internal sealed record MaxPositionSizeParams(decimal MaxNotional);

internal sealed class MaxPositionSizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "max_position_size";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MaxPositionSizeParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.MaxNotional <= decimal.Zero) return Task.CompletedTask;

        for (var i = state.Actions.Count - 1; i >= 0; i--)
        {
            var action = state.Actions[i];
            if (!string.Equals(action.Intent, "BUY", StringComparison.OrdinalIgnoreCase))
                continue;

            var notional = action.Quantity * state.Context.CurrentPrice;
            if (notional > p.MaxNotional)
            {
                // 缩放到最大允许值
                action.Quantity = p.MaxNotional / state.Context.CurrentPrice;
            }
        }

        return Task.CompletedTask;
    }
}

// ── max_pyramiding ──
internal sealed record MaxPyramidingParams(int MaxLevel);

internal sealed class MaxPyramidingNode(JsonElement @params) : IRuleNode
{
    public string Kind => "max_pyramiding";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MaxPyramidingParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var currentLevel = state.Context.Position?.LotCount ?? 0;
        if (currentLevel >= p.MaxLevel)
        {
            // 移除所有开仓操作
            state.Actions.RemoveAll(a => string.Equals(a.Intent, "BUY", StringComparison.OrdinalIgnoreCase));
        }

        return Task.CompletedTask;
    }
}

// ── max_drawdown ──
internal sealed record MaxDrawdownParams(decimal MaxDrawdownPercent);

internal sealed class MaxDrawdownNode(JsonElement @params) : IRuleNode
{
    public string Kind => "max_drawdown";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MaxDrawdownParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var drawdown = state.Context.Portfolio?.Drawdown ?? decimal.Zero;
        if (drawdown >= p.MaxDrawdownPercent)
        {
            state.Actions.Clear();
            state.Terminated = true;
        }

        return Task.CompletedTask;
    }
}

// ── daily_loss_limit ──
internal sealed record DailyLossLimitParams(decimal MaxDailyLoss);

internal sealed class DailyLossLimitNode(JsonElement @params) : IRuleNode
{
    public string Kind => "daily_loss_limit";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<DailyLossLimitParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var dailyPnl = state.Context.Portfolio?.DailyPnl ?? decimal.Zero;
        // DailyPnl 为负即亏损
        if (dailyPnl < 0 && Math.Abs(dailyPnl) >= p.MaxDailyLoss)
        {
            state.Actions.Clear();
            state.Terminated = true;
        }

        return Task.CompletedTask;
    }
}

// ── cooldown ──
internal sealed record CooldownParams(int CooldownMinutes);

internal sealed class CooldownNode(JsonElement @params) : IRuleNode
{
    public string Kind => "cooldown";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => [];

    public async Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<CooldownParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.CooldownMinutes <= 0) return;

        var store = state.Context.StateStore;
        if (store is null) return;

        var nodeState = await store.ReadStateAsync(state.Context.ScopeKey, Kind, ct);
        if (nodeState?.Data.TryGetValue("lastTradeAt", out var lastTradeEl) == true)
        {
            var lastTradeAt = lastTradeEl.GetDateTime();
            if (state.Context.EvaluationTime - lastTradeAt < TimeSpan.FromMinutes(p.CooldownMinutes))
            {
                // 冷却期内清空所有 BUY/SELL 操作
                state.Actions.RemoveAll(a =>
                    string.Equals(a.Intent, "BUY", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Intent, "SELL", StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}

// ── consecutive_loss_stop ──
internal sealed record ConsecutiveLossStopParams(int MaxConsecutiveLosses);

internal sealed class ConsecutiveLossStopNode(JsonElement @params) : IRuleNode
{
    public string Kind => "consecutive_loss_stop";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => [];

    public async Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<ConsecutiveLossStopParams>(@params, RuleJsonOptions.Default);
        if (p is null) return;

        var store = state.Context.StateStore;
        if (store is null) return;

        var nodeState = await store.ReadStateAsync(state.Context.ScopeKey, Kind, ct);
        var consecutiveLosses = 0;
        if (nodeState?.Data.TryGetValue("consecutiveLosses", out var clEl) == true)
            consecutiveLosses = clEl.GetInt32();

        if (consecutiveLosses >= p.MaxConsecutiveLosses)
        {
            state.Actions.Clear();
            state.Terminated = true;
        }
    }
}

// ── quality_filter ──
internal sealed record QualityFilterParams(Dictionary<string, decimal> DegradeMap);

internal sealed class QualityFilterNode(JsonElement @params) : IRuleNode
{
    public string Kind => "quality_filter";
    public RulePhase Phase => RulePhase.Risk;
    // Deps 由 degradeMap 的键决定
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<QualityFilterParams>(@params, RuleJsonOptions.Default);
            return p?.DegradeMap is not null ? [.. p.DegradeMap.Keys] : [];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<QualityFilterParams>(@params, RuleJsonOptions.Default);
        if (p?.DegradeMap is not { Count: > 0 }) return Task.CompletedTask;

        // 计算综合质量系数：信号值 * degradeValue 的均值
        var totalWeight = decimal.Zero;
        var count = 0;
        foreach (var (signalKey, degradeValue) in p.DegradeMap)
        {
            if (state.Signals.TryGetValue(signalKey, out var sig))
            {
                totalWeight += sig.Value * degradeValue;
                count++;
            }
        }

        var quality = count > 0 ? totalWeight / count : 0m;

        // 按质量系数缩放 Actions 的 Quantity
        for (var i = state.Actions.Count - 1; i >= 0; i--)
        {
            var action = state.Actions[i];
            if (quality <= decimal.Zero)
            {
                state.Actions.RemoveAt(i);
            }
            else
            {
                action.Quantity *= quality;
            }
        }

        return Task.CompletedTask;
    }
}

// ── max_positions ──
internal sealed record MaxPositionsParams(int MaxCount, string Scope); // "global" / "exchange" / "pair"

internal sealed class MaxPositionsNode(JsonElement @params) : IRuleNode
{
    public string Kind => "max_positions";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MaxPositionsParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.MaxCount <= 0) return Task.CompletedTask;

        var openPositions = state.Context.Portfolio?.OpenPositions ?? 0;

        // scope 当前简化处理：总是使用 Portfolio.OpenPositions
        if (openPositions >= p.MaxCount)
        {
            state.Actions.RemoveAll(a =>
                string.Equals(a.Intent, "BUY", StringComparison.OrdinalIgnoreCase));
        }

        return Task.CompletedTask;
    }
}

// ── max_correlation ──
internal sealed record MaxCorrelationParams(decimal MaxCorrelation);

internal sealed class MaxCorrelationNode(JsonElement @params) : IRuleNode
{
    public string Kind => "max_correlation";
    public RulePhase Phase => RulePhase.Risk;
    public IReadOnlyList<string> Deps => ["CORRELATION"];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MaxCorrelationParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var corr = decimal.Zero;
        if (state.DerivedValues.TryGetValue("CORRELATION", out var dv))
            corr = dv;
        else if (state.Signals.TryGetValue("CORRELATION", out var sig))
            corr = sig.Value;

        if (Math.Abs(corr) >= p.MaxCorrelation)
        {
            // 相关性太高，清空所有新开仓
            state.Actions.RemoveAll(a =>
                string.Equals(a.Intent, "BUY", StringComparison.OrdinalIgnoreCase));
        }

        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class RiskNodesRegistration
{
    public static void RegisterRiskNodes(this NodeRegistry reg)
    {
        reg.Register("max_position_size", p => new MaxPositionSizeNode(p));
        reg.Register("max_pyramiding", p => new MaxPyramidingNode(p));
        reg.Register("max_drawdown", p => new MaxDrawdownNode(p));
        reg.Register("daily_loss_limit", p => new DailyLossLimitNode(p));
        reg.Register("cooldown", p => new CooldownNode(p));
        reg.Register("consecutive_loss_stop", p => new ConsecutiveLossStopNode(p));
        reg.Register("quality_filter", p => new QualityFilterNode(p));
        reg.Register("max_positions", p => new MaxPositionsNode(p));
        reg.Register("max_correlation", p => new MaxCorrelationNode(p));
    }
}
