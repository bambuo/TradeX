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
        reg.Register("regime_gate", new NodeDescriptor
        {
            Kind = "regime_gate", Phase = RulePhase.Gate,
            Description = "市场体制匹配：只在指定的市场体制下放行",
            Category = "Gate",
            Params = [
                new() { Name = "allowedRegimes", Type = "string[]", Required = true,
                    Enum = ["RANGING", "TRENDING", "HIGH_VOL", "CRASH", "LOW_VOL"],
                    Description = "允许执行的市场体制列表" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "仅震荡行情", ["params"] = new Dictionary<string, object> { ["allowedRegimes"] = new[] { "RANGING" } } }
            ]
        }, p => new RegimeGateNode(p));

        reg.Register("time_gate", new NodeDescriptor
        {
            Kind = "time_gate", Phase = RulePhase.Gate,
            Description = "时间门：在指定时间周期/区间内放行",
            Category = "Gate",
            Params = [
                new() { Name = "mode", Type = "string", Required = true,
                    Enum = ["WINDOW", "INTERVAL"], Description = "时间模式" },
                new() { Name = "windows", Type = "string[]", Required = false,
                    Description = "时间窗口列表 (mode=WINDOW 时)" },
                new() { Name = "intervalMinutes", Type = "int", Required = false, Default = 60,
                    Min = 1, Description = "执行间隔（分钟）", Unit = "min" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "每 4 小时", ["params"] = new Dictionary<string, object> { ["mode"] = "INTERVAL", ["intervalMinutes"] = 240 } }
            ]
        }, p => new TimeGateNode(p));

        reg.Register("pair_gate", new NodeDescriptor
        {
            Kind = "pair_gate", Phase = RulePhase.Gate,
            Description = "交易对白名单：仅指定交易对放行",
            Category = "Gate",
            Params = [
                new() { Name = "whitelist", Type = "string[]", Required = true,
                    Description = "允许的交易对列表" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "仅 BTC/USDT", ["params"] = new Dictionary<string, object> { ["whitelist"] = new[] { "BTCUSDT" } } }
            ]
        }, p => new PairGateNode(p));

        reg.Register("signal_gate", new NodeDescriptor
        {
            Kind = "signal_gate", Phase = RulePhase.Gate,
            Description = "信号门：根据信号值与阈值的比较结果放行",
            Category = "Gate",
            Params = [
                new() { Name = "signal", Type = "ref", Required = true, RefScope = "signal",
                    Description = "信号名称" },
                new() { Name = "op", Type = "string", Required = true,
                    Enum = ["<=", ">=", "<", ">", "=="], Description = "比较运算符" },
                new() { Name = "threshold", Type = "float", Required = true,
                    Description = "阈值" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "RSI 超卖", ["params"] = new Dictionary<string, object> { ["signal"] = "RSI_14", ["op"] = "<=", ["threshold"] = 30m } }
            ]
        }, p => new SignalGateNode(p));

        reg.Register("capital_gate", new NodeDescriptor
        {
            Kind = "capital_gate", Phase = RulePhase.Gate,
            Description = "资金门：可用资金不足时拒绝交易",
            Category = "Gate",
            Params = [
                new() { Name = "minAvailableCash", Type = "float", Required = true,
                    Min = 0, Description = "最低可用资金", Unit = "USDT" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "至少 100 USDT", ["params"] = new Dictionary<string, object> { ["minAvailableCash"] = 100m } }
            ]
        }, p => new CapitalGateNode(p));

        reg.Register("position_gate", new NodeDescriptor
        {
            Kind = "position_gate", Phase = RulePhase.Gate,
            Description = "持仓状态门：仅在指定持仓状态下放行",
            Category = "Gate",
            Params = [
                new() { Name = "require", Type = "string", Required = true,
                    Enum = ["OPEN", "CLOSED", "ANY"], Description = "要求的持仓状态" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "空仓才放行", ["params"] = new Dictionary<string, object> { ["require"] = "CLOSED" } },
                new Dictionary<string, object> { ["title"] = "持仓才放行", ["params"] = new Dictionary<string, object> { ["require"] = "OPEN" } }
            ]
        }, p => new PositionGateNode(p));
    }
}
