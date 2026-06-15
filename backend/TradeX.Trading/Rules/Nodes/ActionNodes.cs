using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// Action 阶段节点 — 决策生成，追加 ActionDecision
// ═══════════════════════════════════════════════════════════════════

// ── signal_action ──
internal sealed record SignalActionParams(
    string BuySignal,
    string SellSignal,
    decimal Threshold,
    string Direction); // "ABOVE" / "BELOW"

internal sealed class SignalActionNode(JsonElement @params) : IRuleNode
{
    public string Kind => "signal_action";
    public RulePhase Phase => RulePhase.Action;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<SignalActionParams>(@params, RuleJsonOptions.Default);
            var list = new List<string>(2);
            if (p?.BuySignal is { Length: > 0 }) list.Add(p.BuySignal);
            if (p?.SellSignal is { Length: > 0 }) list.Add(p.SellSignal);
            return list;
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<SignalActionParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var isAbove = string.Equals(p.Direction, "ABOVE", StringComparison.OrdinalIgnoreCase);

        // 买入信号检查
        if (!string.IsNullOrWhiteSpace(p.BuySignal) &&
            state.Signals.TryGetValue(p.BuySignal, out var buySig))
        {
            var triggered = isAbove ? buySig.Value > p.Threshold : buySig.Value < p.Threshold;
            if (triggered && state.SizeDecisions.Count > 0)
            {
                foreach (var sd in state.SizeDecisions.Where(s => s.Intent == "ENTER"))
                {
                    state.Actions.Add(new ActionDecision
                    {
                        Intent = "BUY",
                        Quantity = sd.Amount / state.Context.CurrentPrice,
                        OrderType = "MARKET",
                        Priority = 10,
                        Pair = state.Context.Pair,
                        Reason = $"signal_action:BUY:{p.BuySignal}={buySig.Value}"
                    });
                }
            }
        }

        // 卖出信号检查
        if (!string.IsNullOrWhiteSpace(p.SellSignal) &&
            state.Signals.TryGetValue(p.SellSignal, out var sellSig))
        {
            var triggered = isAbove ? sellSig.Value > p.Threshold : sellSig.Value < p.Threshold;
            if (triggered)
            {
                var pos = state.Context.Position;
                if (pos?.HasPosition() == true)
                {
                    state.Actions.Add(new ActionDecision
                    {
                        Intent = "SELL",
                        Quantity = Math.Abs(pos.Quantity),
                        OrderType = "MARKET",
                        Priority = 10,
                        Pair = state.Context.Pair,
                        Reason = $"signal_action:SELL:{p.SellSignal}={sellSig.Value}"
                    });
                }
            }
        }

        return Task.CompletedTask;
    }
}

// ── grid_action ──
internal sealed record GridActionParams(decimal DeviationPercent, decimal BasePrice);

internal sealed class GridActionNode(JsonElement @params) : IRuleNode
{
    public string Kind => "grid_action";
    public RulePhase Phase => RulePhase.Action;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<GridActionParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.BasePrice <= decimal.Zero) return Task.CompletedTask;

        var basePrice = p.BasePrice;
        var currentPrice = state.Context.CurrentPrice;
        var deviation = (currentPrice - basePrice) / basePrice * 100m;

        // 偏离超过阈值 → 反向操作再平衡
        if (Math.Abs(deviation) >= p.DeviationPercent)
        {
            var intent = deviation > 0 ? "SELL" : "BUY";

            // 取网格 SizeDecision 中对应的网格层
            var sizeDec = state.SizeDecisions.FirstOrDefault();
            var quantity = sizeDec?.Amount / currentPrice ?? decimal.Zero;

            state.Actions.Add(new ActionDecision
            {
                Intent = intent,
                Quantity = quantity,
                OrderType = "MARKET",
                Priority = 20,
                Pair = state.Context.Pair,
                Reason = $"grid_action:deviation={deviation:F2}%"
            });
        }

        return Task.CompletedTask;
    }
}

// ── trailing_stop_action ──
internal sealed record TrailingStopActionParams(decimal TrailPercent, decimal? ActivationPrice);

internal sealed class TrailingStopActionNode(JsonElement @params) : IRuleNode
{
    public string Kind => "trailing_stop_action";
    public RulePhase Phase => RulePhase.Action;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<TrailingStopActionParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var pos = state.Context.Position;
        if (pos?.HasPosition() != true) return Task.CompletedTask;

        var currentPrice = state.Context.CurrentPrice;
        var isLong = pos.Quantity > decimal.Zero;

        // 激活价检查
        if (p.ActivationPrice.HasValue)
        {
            if (isLong && currentPrice < p.ActivationPrice.Value) return Task.CompletedTask;
            if (!isLong && currentPrice > p.ActivationPrice.Value) return Task.CompletedTask;
        }

        var trailPrice = isLong
            ? currentPrice * (1m - p.TrailPercent / 100m)
            : currentPrice * (1m + p.TrailPercent / 100m);

        var triggered = isLong
            ? currentPrice <= trailPrice
            : currentPrice >= trailPrice;

        if (triggered)
        {
            state.Actions.Add(new ActionDecision
            {
                Intent = "SELL",
                Quantity = Math.Abs(pos.Quantity),
                OrderType = "MARKET",
                Priority = 5,
                Pair = state.Context.Pair,
                Reason = $"trailing_stop_action:price={currentPrice},trail={trailPrice}"
            });
        }

        return Task.CompletedTask;
    }
}

// ── take_profit_action ──
internal sealed record TakeProfitActionParams(decimal TpPercent);

internal sealed class TakeProfitActionNode(JsonElement @params) : IRuleNode
{
    public string Kind => "take_profit_action";
    public RulePhase Phase => RulePhase.Action;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<TakeProfitActionParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var pos = state.Context.Position;
        if (pos?.HasPosition() != true || pos.EntryPrice <= decimal.Zero) return Task.CompletedTask;

        var currentPrice = state.Context.CurrentPrice;
        var pnlPercent = (currentPrice - pos.EntryPrice) / pos.EntryPrice * 100m;

        // 多仓：正收益达到阈值止盈；空仓：负收益达到阈值止盈（对空仓收益=entryPrice > currentPrice）
        if (pos.Quantity > decimal.Zero && pnlPercent >= p.TpPercent)
        {
            state.Actions.Add(new ActionDecision
            {
                Intent = "SELL",
                Quantity = pos.Quantity,
                OrderType = "MARKET",
                Priority = 5,
                Pair = state.Context.Pair,
                Reason = $"take_profit_action:p&l={pnlPercent:F2}%"
            });
        }
        else if (pos.Quantity < decimal.Zero && -pnlPercent >= p.TpPercent)
        {
            state.Actions.Add(new ActionDecision
            {
                Intent = "BUY",
                Quantity = Math.Abs(pos.Quantity),
                OrderType = "MARKET",
                Priority = 5,
                Pair = state.Context.Pair,
                Reason = $"take_profit_action:p&l={-pnlPercent:F2}%"
            });
        }

        return Task.CompletedTask;
    }
}

// ── dca_action ──
internal sealed record DcaActionParams(int IntervalHours, decimal Amount);

internal sealed class DcaActionNode(JsonElement @params) : IRuleNode
{
    public string Kind => "dca_action";
    public RulePhase Phase => RulePhase.Action;
    public IReadOnlyList<string> Deps => [];

    public async Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<DcaActionParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.Amount <= decimal.Zero || p.IntervalHours <= 0) return;

        // 从 StateStore 读取上次执行时间
        var store = state.Context.StateStore;
        if (store is null) return;

        var nodeState = await store.ReadStateAsync(state.Context.ScopeKey, Kind, ct);
        if (nodeState?.Data.TryGetValue("lastAt", out var lastAtEl) == true)
        {
            var lastAt = lastAtEl.GetDateTime();
            if (state.Context.EvaluationTime - lastAt < TimeSpan.FromHours(p.IntervalHours))
                return;
        }

        var quantity = p.Amount / state.Context.CurrentPrice;
        if (quantity <= decimal.Zero) return;

        state.Actions.Add(new ActionDecision
        {
            Intent = "BUY",
            Quantity = quantity,
            OrderType = "MARKET",
            Priority = 30,
            Pair = state.Context.Pair,
            Reason = $"dca_action:interval={p.IntervalHours}h"
        });
    }
}

// ── trend_action ──
internal sealed record TrendActionParams(string TrendSignal, decimal Threshold, string Direction);

internal sealed class TrendActionNode(JsonElement @params) : IRuleNode
{
    public string Kind => "trend_action";
    public RulePhase Phase => RulePhase.Action;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<TrendActionParams>(@params, RuleJsonOptions.Default);
            return p?.TrendSignal is { Length: > 0 } ? [p.TrendSignal] : [];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<TrendActionParams>(@params, RuleJsonOptions.Default);
        if (p is null || string.IsNullOrWhiteSpace(p.TrendSignal)) return Task.CompletedTask;

        if (!state.Signals.TryGetValue(p.TrendSignal, out var trendSig))
            return Task.CompletedTask;

        var isAbove = string.Equals(p.Direction, "ABOVE", StringComparison.OrdinalIgnoreCase);
        var triggered = isAbove ? trendSig.Value > p.Threshold : trendSig.Value < p.Threshold;

        if (!triggered) return Task.CompletedTask;

        var pos = state.Context.Position;
        var hasPosition = pos?.HasPosition() == true;

        if (!hasPosition)
        {
            // 无仓位 → 顺势开仓
            var sizeDec = state.SizeDecisions.FirstOrDefault();
            var quantity = sizeDec?.Amount / state.Context.CurrentPrice ?? decimal.Zero;

            if (quantity > decimal.Zero)
            {
                state.Actions.Add(new ActionDecision
                {
                    Intent = "BUY",
                    Quantity = quantity,
                    OrderType = "MARKET",
                    Priority = 15,
                    Pair = state.Context.Pair,
                    Reason = $"trend_action:BUY:{p.TrendSignal}={trendSig.Value}"
                });
            }
        }

        return Task.CompletedTask;
    }
}

// ── martingale_action ──
internal sealed record MartingaleActionParams(decimal BaseAmount, decimal Multiplier, int MaxSteps);

internal sealed class MartingaleActionNode(JsonElement @params) : IRuleNode
{
    public string Kind => "martingale_action";
    public RulePhase Phase => RulePhase.Action;
    public IReadOnlyList<string> Deps => [];

    public async Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<MartingaleActionParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.BaseAmount <= decimal.Zero) return;

        var store = state.Context.StateStore;
        if (store is null) return;

        var nodeState = await store.ReadStateAsync(state.Context.ScopeKey, Kind, ct);
        var step = 0;
        if (nodeState?.Data.TryGetValue("step", out var stepEl) == true)
        {
            step = stepEl.GetInt32();
            if (step >= p.MaxSteps) return;
        }

        var amount = p.BaseAmount * (decimal)Math.Pow((double)p.Multiplier, step);
        var quantity = amount / state.Context.CurrentPrice;
        if (quantity <= decimal.Zero) return;

        state.Actions.Add(new ActionDecision
        {
            Intent = "BUY",
            Quantity = quantity,
            OrderType = "MARKET",
            Priority = 35,
            Pair = state.Context.Pair,
            Reason = $"martingale_action:step={step}"
        });
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class ActionNodesRegistration
{
    public static void RegisterActionNodes(this NodeRegistry reg)
    {
        reg.Register("signal_action", p => new SignalActionNode(p));
        reg.Register("grid_action", p => new GridActionNode(p));
        reg.Register("trailing_stop_action", p => new TrailingStopActionNode(p));
        reg.Register("take_profit_action", p => new TakeProfitActionNode(p));
        reg.Register("dca_action", p => new DcaActionNode(p));
        reg.Register("trend_action", p => new TrendActionNode(p));
        reg.Register("martingale_action", p => new MartingaleActionNode(p));
    }
}
