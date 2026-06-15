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
        reg.Register("max_position_size", new NodeDescriptor
        {
            Kind = "max_position_size", Phase = RulePhase.Risk,
            Description = "最大仓位限制：买单名义值超过上限时缩放",
            Category = "Risk",
            Params = [
                new() { Name = "maxNotional", Type = "float", Required = true,
                    Min = 0, Description = "最大名义值", Unit = "USDT" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "单笔不超过 1000 USDT", ["params"] = new Dictionary<string, object> { ["maxNotional"] = 1000m } }
            ]
        }, p => new MaxPositionSizeNode(p));

        reg.Register("max_pyramiding", new NodeDescriptor
        {
            Kind = "max_pyramiding", Phase = RulePhase.Risk,
            Description = "最大加仓层数：达到层数上限后禁止新开仓",
            Category = "Risk",
            Params = [
                new() { Name = "maxLevel", Type = "int", Required = true,
                    Min = 1, Description = "最大加仓层数" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "最多 3 层加仓", ["params"] = new Dictionary<string, object> { ["maxLevel"] = 3 } }
            ]
        }, p => new MaxPyramidingNode(p));

        reg.Register("max_drawdown", new NodeDescriptor
        {
            Kind = "max_drawdown", Phase = RulePhase.Risk,
            Description = "最大回撤：回撤超过阈值时清空操作并终止",
            Category = "Risk",
            Params = [
                new() { Name = "maxDrawdownPercent", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "最大回撤百分比", Unit = "%" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "20% 回撤熔断", ["params"] = new Dictionary<string, object> { ["maxDrawdownPercent"] = 20m } }
            ]
        }, p => new MaxDrawdownNode(p));

        reg.Register("daily_loss_limit", new NodeDescriptor
        {
            Kind = "daily_loss_limit", Phase = RulePhase.Risk,
            Description = "单日亏损上限：当日亏损超过限额时终止",
            Category = "Risk",
            Params = [
                new() { Name = "maxDailyLoss", Type = "float", Required = true,
                    Min = 0, Description = "单日最大亏损", Unit = "USDT" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "日亏损不超过 500 USDT", ["params"] = new Dictionary<string, object> { ["maxDailyLoss"] = 500m } }
            ]
        }, p => new DailyLossLimitNode(p));

        reg.Register("cooldown", new NodeDescriptor
        {
            Kind = "cooldown", Phase = RulePhase.Risk,
            Description = "冷却期：上次交易后等待指定时间再允许新交易",
            Category = "Risk",
            Params = [
                new() { Name = "cooldownMinutes", Type = "int", Required = true,
                    Min = 1, Description = "冷却时间", Unit = "min" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "15 分钟冷却", ["params"] = new Dictionary<string, object> { ["cooldownMinutes"] = 15 } }
            ]
        }, p => new CooldownNode(p));

        reg.Register("consecutive_loss_stop", new NodeDescriptor
        {
            Kind = "consecutive_loss_stop", Phase = RulePhase.Risk,
            Description = "连续亏损停止：连续亏损次数达到上限时终止",
            Category = "Risk",
            Params = [
                new() { Name = "maxConsecutiveLosses", Type = "int", Required = true,
                    Min = 1, Description = "最大连续亏损次数" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "连续 3 次亏损停止", ["params"] = new Dictionary<string, object> { ["maxConsecutiveLosses"] = 3 } }
            ]
        }, p => new ConsecutiveLossStopNode(p));

        reg.Register("quality_filter", new NodeDescriptor
        {
            Kind = "quality_filter", Phase = RulePhase.Risk,
            Description = "质量过滤：根据信号质量系数缩放操作数量",
            Category = "Risk",
            Params = [
                new() { Name = "degradeMap", Type = "object", Required = true,
                    Description = "信号名→权重映射，如 {\"RSI_14\": 0.6, \"MACD_HIST\": 0.4}" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "RSI+MACD 质量评估", ["params"] = new Dictionary<string, object> { ["degradeMap"] = new Dictionary<string, decimal> { ["RSI_14"] = 0.6m, ["MACD_HIST"] = 0.4m } } }
            ]
        }, p => new QualityFilterNode(p));

        reg.Register("max_positions", new NodeDescriptor
        {
            Kind = "max_positions", Phase = RulePhase.Risk,
            Description = "最大持仓数：超过上限后禁止新开仓",
            Category = "Risk",
            Params = [
                new() { Name = "maxCount", Type = "int", Required = true,
                    Min = 1, Description = "最大同时持仓数" },
                new() { Name = "scope", Type = "string", Required = false, Default = "global",
                    Enum = ["global", "exchange", "pair"], Description = "统计范围" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "全局最多 5 个持仓", ["params"] = new Dictionary<string, object> { ["maxCount"] = 5, ["scope"] = "global" } }
            ]
        }, p => new MaxPositionsNode(p));

        reg.Register("max_correlation", new NodeDescriptor
        {
            Kind = "max_correlation", Phase = RulePhase.Risk,
            Description = "最大相关性：与现有持仓相关性过高时禁止开仓",
            Category = "Risk",
            Params = [
                new() { Name = "maxCorrelation", Type = "float", Required = true,
                    Min = 0, Max = 1, Description = "最大允许相关性 (0~1)" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "相关性不超过 0.8", ["params"] = new Dictionary<string, object> { ["maxCorrelation"] = 0.8m } }
            ]
        }, p => new MaxCorrelationNode(p));
    }
}
