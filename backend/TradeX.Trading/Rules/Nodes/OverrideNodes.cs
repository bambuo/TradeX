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

        // 紧急平仓：追加 SELL_ALL，清空其他 Actions
        var pos = state.Context.Position;
        if (pos?.HasPosition() == true)
        {
            state.Actions.Clear();
            state.Actions.Add(new ActionDecision
            {
                Intent = "SELL_ALL",
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
        reg.Register("kill_switch", p => new KillSwitchNode(p));
        reg.Register("emergency_exit", p => new EmergencyExitNode(p));
        reg.Register("manual_block", p => new ManualBlockNode(p));
        reg.Register("exchange_health", p => new ExchangeHealthNode(p));
    }
}
