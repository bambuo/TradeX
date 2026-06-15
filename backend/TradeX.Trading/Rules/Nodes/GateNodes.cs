using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// Gate 阶段节点 — 条件门，决定链条是否继续执行
// ═══════════════════════════════════════════════════════════════════

// ── regime_gate ──
internal sealed record RegimeGateParams(string[] AllowedRegimes);

internal sealed class RegimeGateNode(JsonElement @params) : IRuleNode
{
    public string Kind => "regime_gate";
    public RulePhase Phase => RulePhase.Gate;
    public IReadOnlyList<string> Deps => ["MARKET_REGIME"];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<RegimeGateParams>(@params, RuleJsonOptions.Default);
        if (p is not { AllowedRegimes.Length: > 0 }) return Task.CompletedTask;

        if (!state.Signals.TryGetValue("MARKET_REGIME", out var regimeSig)) return Task.CompletedTask;

        var regime = ((int)regimeSig.Value).ToString();
        if (!p.AllowedRegimes.Contains(regime, StringComparer.OrdinalIgnoreCase))
            state.Blocked = true;

        return Task.CompletedTask;
    }
}

// ── time_gate ──
internal sealed record TimeWindow(int DayOfWeek, string StartTime, string EndTime); // 0=Sun..6=Sat
internal sealed record TimeGateParams(string Mode, TimeWindow[]? Windows, int IntervalMinutes = 60);

internal sealed class TimeGateNode(JsonElement @params) : IRuleNode
{
    public string Kind => "time_gate";
    public RulePhase Phase => RulePhase.Gate;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<TimeGateParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var now = state.Context.EvaluationTime;

        if (string.Equals(p.Mode, "WINDOW", StringComparison.OrdinalIgnoreCase) && p.Windows is { Length: > 0 })
        {
            var dayOfWeek = (int)now.DayOfWeek; // 0=Sun matches TimeWindow.DayOfWeek convention
            foreach (var w in p.Windows)
            {
                if (w.DayOfWeek != dayOfWeek) continue;
                if (TimeOnly.TryParse(w.StartTime, out var start) &&
                    TimeOnly.TryParse(w.EndTime, out var end) &&
                    TimeOnly.FromDateTime(now) >= start &&
                    TimeOnly.FromDateTime(now) <= end)
                    return Task.CompletedTask;
            }
            state.Blocked = true;
        }
        else if (string.Equals(p.Mode, "INTERVAL", StringComparison.OrdinalIgnoreCase))
        {
            var interval = p.IntervalMinutes > 0 ? p.IntervalMinutes : 60;
            var minutesSinceMidnight = (int)now.TimeOfDay.TotalMinutes;
            if (minutesSinceMidnight % interval != 0)
                state.Blocked = true;
        }

        return Task.CompletedTask;
    }
}

// ── pair_gate ──
internal sealed record PairGateParams(string[] Whitelist);

internal sealed class PairGateNode(JsonElement @params) : IRuleNode
{
    public string Kind => "pair_gate";
    public RulePhase Phase => RulePhase.Gate;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PairGateParams>(@params, RuleJsonOptions.Default);
        if (p is not { Whitelist.Length: > 0 }) return Task.CompletedTask;

        if (!p.Whitelist.Contains(state.Context.Pair, StringComparer.OrdinalIgnoreCase))
            state.Blocked = true;

        return Task.CompletedTask;
    }
}

// ── signal_gate ──
internal sealed record SignalGateParams(string Signal, string Op, decimal Threshold);

internal sealed class SignalGateNode(JsonElement @params) : IRuleNode
{
    public string Kind => "signal_gate";
    public RulePhase Phase => RulePhase.Gate;
    // Deps 由配置的 Signal 决定，运行时动态声明
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<SignalGateParams>(@params, RuleJsonOptions.Default);
            return p?.Signal is { Length: > 0 } ? [p.Signal] : [];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<SignalGateParams>(@params, RuleJsonOptions.Default);
        if (p is null || string.IsNullOrWhiteSpace(p.Signal)) return Task.CompletedTask;

        if (!state.Signals.TryGetValue(p.Signal, out var sig)) return Task.CompletedTask;

        var match = p.Op switch
        {
            ">" => sig.Value > p.Threshold,
            "<" => sig.Value < p.Threshold,
            ">=" => sig.Value >= p.Threshold,
            "<=" => sig.Value <= p.Threshold,
            "==" => sig.Value == p.Threshold,
            _ => true
        };

        if (!match) state.Blocked = true;

        return Task.CompletedTask;
    }
}

// ── capital_gate ──
internal sealed record CapitalGateParams(decimal MinAvailableCash);

internal sealed class CapitalGateNode(JsonElement @params) : IRuleNode
{
    public string Kind => "capital_gate";
    public RulePhase Phase => RulePhase.Gate;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<CapitalGateParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var cash = state.Context.Portfolio?.AvailableCash ?? decimal.Zero;
        if (cash < p.MinAvailableCash)
            state.Blocked = true;

        return Task.CompletedTask;
    }
}

// ── position_gate ──
internal sealed record PositionGateParams(string Require); // "OPEN" / "CLOSED" / "ANY"

internal sealed class PositionGateNode(JsonElement @params) : IRuleNode
{
    public string Kind => "position_gate";
    public RulePhase Phase => RulePhase.Gate;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PositionGateParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var hasPosition = state.Context.Position?.HasPosition() == true;

        var blocked = p.Require.ToUpperInvariant() switch
        {
            "OPEN" => !hasPosition,
            "CLOSED" => hasPosition,
            "ANY" => false,
            _ => false
        };

        if (blocked) state.Blocked = true;

        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class GateNodesRegistration
{
    public static void RegisterGateNodes(this NodeRegistry reg)
    {
        reg.Register("regime_gate", p => new RegimeGateNode(p));
        reg.Register("time_gate", p => new TimeGateNode(p));
        reg.Register("pair_gate", p => new PairGateNode(p));
        reg.Register("signal_gate", p => new SignalGateNode(p));
        reg.Register("capital_gate", p => new CapitalGateNode(p));
        reg.Register("position_gate", p => new PositionGateNode(p));
    }
}
