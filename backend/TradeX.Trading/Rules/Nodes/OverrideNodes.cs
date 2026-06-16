using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// Override 阶段节点 — 覆盖/熔断，清空 Actions + Terminated
// ═══════════════════════════════════════════════════════════════════

// ── kill_switch ──
internal sealed record KillSwitchParams(string Key);

internal sealed class KillSwitchNode(JsonElement @params) : IRuleNode
{
    public string Kind => "kill_switch";
    public RulePhase Phase => RulePhase.Override;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<KillSwitchParams>(@params, RuleJsonOptions.Default);
        if (p is null || string.IsNullOrWhiteSpace(p.Key)) return Task.CompletedTask;

        var isActive = state.Context.IsKillSwitchActive?.Invoke(p.Key) ?? false;
        if (isActive)
        {
            state.Actions.Clear();
            state.Terminated = true;
        }

        return Task.CompletedTask;
    }
}

// ── emergency_exit ──
internal sealed record EmergencyExitParams(string Signal, decimal Threshold, string Op);

internal sealed class EmergencyExitNode(JsonElement @params) : IRuleNode
{
    public string Kind => "emergency_exit";
    public RulePhase Phase => RulePhase.Override;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<EmergencyExitParams>(@params, RuleJsonOptions.Default);
            return p?.Signal is { Length: > 0 } ? [p.Signal] : [];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<EmergencyExitParams>(@params, RuleJsonOptions.Default);
        if (p is null || string.IsNullOrWhiteSpace(p.Signal)) return Task.CompletedTask;

        if (!state.Signals.TryGetValue(p.Signal, out var sig)) return Task.CompletedTask;

        var triggered = p.Op switch
        {
            ">" => sig.Value > p.Threshold,
            "<" => sig.Value < p.Threshold,
            ">=" => sig.Value >= p.Threshold,
            "<=" => sig.Value <= p.Threshold,
            "==" => sig.Value == p.Threshold,
            _ => false
        };

        if (!triggered) return Task.CompletedTask;

        // 紧急平仓：追加 SELL_ALL（多）或 BUY（空），清空其他 Actions
        var pos = state.Context.Position;
        if (pos?.HasPosition() == true)
        {
            state.Actions.Clear();
            var isLong = pos.Quantity > decimal.Zero;
            state.Actions.Add(new ActionDecision
            {
                Intent = isLong ? "SELL_ALL" : "BUY",
                Quantity = Math.Abs(pos.Quantity),
                OrderType = "MARKET",
                Priority = 0,
                Pair = state.Context.Pair,
                Reason = $"emergency_exit:{p.Signal}={sig.Value}"
            });
            state.Terminated = true;
        }

        return Task.CompletedTask;
    }
}

// ── manual_block ──
internal sealed record ManualBlockParams(string BlockedKey);

internal sealed class ManualBlockNode(JsonElement @params) : IRuleNode
{
    public string Kind => "manual_block";
    public RulePhase Phase => RulePhase.Override;
    public IReadOnlyList<string> Deps => [];

    public async Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<ManualBlockParams>(@params, RuleJsonOptions.Default);
        if (p is null || string.IsNullOrWhiteSpace(p.BlockedKey)) return;

        var store = state.Context.StateStore;
        if (store is null) return;

        var nodeState = await store.ReadStateAsync(state.Context.ScopeKey, Kind, ct);
        if (nodeState?.Data.TryGetValue(p.BlockedKey, out var blockedEl) == true)
        {
            if (blockedEl.ValueKind == JsonValueKind.True)
            {
                state.Actions.Clear();
                state.Terminated = true;
            }
        }
    }
}

// ── exchange_health ──
internal sealed record ExchangeHealthParams(decimal MaxLatencyMs);

internal sealed class ExchangeHealthNode(JsonElement @params) : IRuleNode
{
    public string Kind => "exchange_health";
    public RulePhase Phase => RulePhase.Override;
    public IReadOnlyList<string> Deps => ["EXCHANGE_LATENCY"];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<ExchangeHealthParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.MaxLatencyMs <= decimal.Zero) return Task.CompletedTask;

        if (!state.Signals.TryGetValue("EXCHANGE_LATENCY", out var latencySig))
            return Task.CompletedTask;

        if (latencySig.Value > p.MaxLatencyMs)
        {
            state.Actions.Clear();
            state.Terminated = true;
        }

        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class OverrideNodesRegistration
{
    public static void RegisterOverrideNodes(this NodeRegistry reg)
    {
        reg.Register("kill_switch", new NodeDescriptor
        {
            Kind = "kill_switch", Phase = RulePhase.Override,
            Description = "紧急停止开关：激活时清空所有操作并终止",
            Category = "Override",
            Params = [
                new() { Name = "key", Type = "string", Required = true,
                    Description = "开关标识键" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "全局停止", ["params"] = new Dictionary<string, object> { ["key"] = "GLOBAL_KILL" } }
            ]
        }, p => new KillSwitchNode(p));

        reg.Register("emergency_exit", new NodeDescriptor
        {
            Kind = "emergency_exit", Phase = RulePhase.Override,
            Description = "紧急平仓：信号触发时立即全平并终止",
            Category = "Override",
            Params = [
                new() { Name = "signal", Type = "ref", Required = true, RefScope = "signal",
                    Description = "触发信号名称" },
                new() { Name = "threshold", Type = "float", Required = true,
                    Description = "触发阈值" },
                new() { Name = "op", Type = "string", Required = true,
                    Enum = ["<=", ">=", "<", ">", "=="], Description = "比较运算符" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "RSI 超买紧急平仓", ["params"] = new Dictionary<string, object> { ["signal"] = "RSI_14", ["op"] = ">=", ["threshold"] = 90m } }
            ]
        }, p => new EmergencyExitNode(p));

        reg.Register("manual_block", new NodeDescriptor
        {
            Kind = "manual_block", Phase = RulePhase.Override,
            Description = "手动屏蔽：通过外部状态手动阻止交易",
            Category = "Override",
            Params = [
                new() { Name = "blockedKey", Type = "string", Required = true,
                    Description = "屏蔽状态键名 (在 StateStore 中查找)" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "手动暂停", ["params"] = new Dictionary<string, object> { ["blockedKey"] = "MANUAL_BLOCK" } }
            ]
        }, p => new ManualBlockNode(p));

        reg.Register("exchange_health", new NodeDescriptor
        {
            Kind = "exchange_health", Phase = RulePhase.Override,
            Description = "交易所健康检查：延迟超过阈值时终止",
            Category = "Override",
            Params = [
                new() { Name = "maxLatencyMs", Type = "float", Required = true,
                    Min = 0, Description = "最大允许延迟", Unit = "ms" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "延迟不超过 5 秒", ["params"] = new Dictionary<string, object> { ["maxLatencyMs"] = 5000m } }
            ]
        }, p => new ExchangeHealthNode(p));
    }
}
