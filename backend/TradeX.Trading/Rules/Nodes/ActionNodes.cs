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
        reg.Register("signal_action", new NodeDescriptor
        {
            Kind = "signal_action", Phase = RulePhase.Action,
            Description = "信号动作：根据信号值与阈值的比较生成买卖决策",
            Category = "Action", ProducesDecisions = true,
            Params = [
                new() { Name = "buySignal", Type = "ref", Required = false, RefScope = "signal",
                    Description = "买入信号名称" },
                new() { Name = "sellSignal", Type = "ref", Required = false, RefScope = "signal",
                    Description = "卖出信号名称" },
                new() { Name = "threshold", Type = "float", Required = true,
                    Description = "触发阈值" },
                new() { Name = "direction", Type = "string", Required = true,
                    Enum = ["ABOVE", "BELOW"], Description = "触发方向" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "RSI 超卖买入", ["params"] = new Dictionary<string, object> { ["buySignal"] = "RSI_14", ["threshold"] = 30m, ["direction"] = "BELOW" } }
            ]
        }, p => new SignalActionNode(p));

        reg.Register("grid_action", new NodeDescriptor
        {
            Kind = "grid_action", Phase = RulePhase.Action,
            Description = "网格动作：价格偏离基准超过阈值时反向操作",
            Category = "Action", ProducesDecisions = true,
            Params = [
                new() { Name = "deviationPercent", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "偏离阈值", Unit = "%" },
                new() { Name = "basePrice", Type = "float", Required = true,
                    Min = 0, Description = "基准价格", Unit = "USDT" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "1% 偏离再平衡", ["params"] = new Dictionary<string, object> { ["deviationPercent"] = 1m, ["basePrice"] = 50000m } }
            ]
        }, p => new GridActionNode(p));

        reg.Register("trailing_stop_action", new NodeDescriptor
        {
            Kind = "trailing_stop_action", Phase = RulePhase.Action,
            Description = "追踪止损动作：价格回撤超过百分比时平仓",
            Category = "Action", ProducesDecisions = true,
            Params = [
                new() { Name = "trailPercent", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "追踪距离百分比", Unit = "%" },
                new() { Name = "activationPrice", Type = "float", Required = false,
                    Min = 0, Description = "激活价格 (留空立即激活)" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "3% 追踪止损", ["params"] = new Dictionary<string, object> { ["trailPercent"] = 3m } }
            ]
        }, p => new TrailingStopActionNode(p));

        reg.Register("take_profit_action", new NodeDescriptor
        {
            Kind = "take_profit_action", Phase = RulePhase.Action,
            Description = "止盈动作：盈利达到百分比阈值时平仓",
            Category = "Action", ProducesDecisions = true,
            Params = [
                new() { Name = "tpPercent", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "止盈百分比", Unit = "%" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "5% 止盈", ["params"] = new Dictionary<string, object> { ["tpPercent"] = 5m } }
            ]
        }, p => new TakeProfitActionNode(p));

        reg.Register("dca_action", new NodeDescriptor
        {
            Kind = "dca_action", Phase = RulePhase.Action,
            Description = "定投动作：按固定时间间隔定期买入",
            Category = "Action", ProducesDecisions = true,
            Params = [
                new() { Name = "intervalHours", Type = "int", Required = true,
                    Min = 1, Description = "定投间隔", Unit = "h" },
                new() { Name = "amount", Type = "float", Required = true,
                    Min = 0, Description = "每次定投金额", Unit = "USDT" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "每 24 小时投 100 USDT", ["params"] = new Dictionary<string, object> { ["intervalHours"] = 24, ["amount"] = 100m } }
            ]
        }, p => new DcaActionNode(p));

        reg.Register("trend_action", new NodeDescriptor
        {
            Kind = "trend_action", Phase = RulePhase.Action,
            Description = "趋势动作：根据趋势信号方向顺势开仓",
            Category = "Action", ProducesDecisions = true,
            Params = [
                new() { Name = "trendSignal", Type = "ref", Required = true, RefScope = "signal",
                    Description = "趋势信号名称" },
                new() { Name = "threshold", Type = "float", Required = true,
                    Description = "触发阈值" },
                new() { Name = "direction", Type = "string", Required = true,
                    Enum = ["ABOVE", "BELOW"], Description = "触发方向" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "MACD 趋势跟踪", ["params"] = new Dictionary<string, object> { ["trendSignal"] = "MACD_HIST", ["threshold"] = 0m, ["direction"] = "ABOVE" } }
            ]
        }, p => new TrendActionNode(p));

        reg.Register("martingale_action", new NodeDescriptor
        {
            Kind = "martingale_action", Phase = RulePhase.Action,
            Description = "马丁格尔动作：每次亏损后加倍买入",
            Category = "Action", ProducesDecisions = true,
            Params = [
                new() { Name = "baseAmount", Type = "float", Required = true,
                    Min = 0, Description = "基础金额", Unit = "USDT" },
                new() { Name = "multiplier", Type = "float", Required = true,
                    Min = 1, Description = "加倍乘数" },
                new() { Name = "maxSteps", Type = "int", Required = true,
                    Min = 1, Description = "最大加倍步数" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "2 倍马丁格尔最多 5 步", ["params"] = new Dictionary<string, object> { ["baseAmount"] = 50m, ["multiplier"] = 2m, ["maxSteps"] = 5 } }
            ]
        }, p => new MartingaleActionNode(p));
    }
}
