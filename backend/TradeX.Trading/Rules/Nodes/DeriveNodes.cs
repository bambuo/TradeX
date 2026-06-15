using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// Derive 阶段节点 — 信号衍生新值，写入 DerivedValues
// ═══════════════════════════════════════════════════════════════════

// ── crossover_check ──
internal sealed record CrossoverCheckParams(string FastSignal, string SlowSignal, string OutputKey);

internal sealed class CrossoverCheckNode(JsonElement @params) : IRuleNode
{
    public string Kind => "crossover_check";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<CrossoverCheckParams>(@params, RuleJsonOptions.Default);
            return p is not null ? [p.FastSignal, p.SlowSignal] : [];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<CrossoverCheckParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        if (!state.Signals.TryGetValue(p.FastSignal, out var fast) ||
            !state.Signals.TryGetValue(p.SlowSignal, out var slow))
            return Task.CompletedTask;

        // fastPrev <= slowPrev && fastCurr > slowCurr → 金叉 (+1)
        // fastPrev >= slowPrev && fastCurr < slowCurr → 死叉 (-1)
        decimal result = 0;
        if (fast.PrevValue <= slow.PrevValue && fast.Value > slow.Value)
            result = 1; // 金叉
        else if (fast.PrevValue >= slow.PrevValue && fast.Value < slow.Value)
            result = -1; // 死叉

        state.DerivedValues[p.OutputKey] = result;
        return Task.CompletedTask;
    }
}

// ── atr_stop_calc ──
internal sealed record AtrStopCalcParams(
    string AtrSignal,
    decimal Multiplier,
    string LongStopKey,
    string LongTpKey,
    string ShortStopKey,
    string ShortTpKey);

internal sealed class AtrStopCalcNode(JsonElement @params) : IRuleNode
{
    public string Kind => "atr_stop_calc";
    public RulePhase Phase => RulePhase.Derive;
    // ATR 信号名可配置
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<AtrStopCalcParams>(@params, RuleJsonOptions.Default);
            return p?.AtrSignal is { Length: > 0 } ? [p.AtrSignal] : ["ATR_14"];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<AtrStopCalcParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var atrKey = string.IsNullOrWhiteSpace(p.AtrSignal) ? "ATR_14" : p.AtrSignal;
        if (!state.Signals.TryGetValue(atrKey, out var atrSig)) return Task.CompletedTask;

        var atr = atrSig.Value;
        var price = state.Context.CurrentPrice;

        state.DerivedValues[p.LongStopKey] = price - atr * p.Multiplier;
        state.DerivedValues[p.LongTpKey] = price + atr * p.Multiplier;
        state.DerivedValues[p.ShortStopKey] = price + atr * p.Multiplier;
        state.DerivedValues[p.ShortTpKey] = price - atr * p.Multiplier;

        return Task.CompletedTask;
    }
}

// ── grid_price_level ──
internal sealed record GridPriceLevelParams(
    decimal TopPrice,
    decimal BottomPrice,
    int GridCount,
    string Mode,     // "LINEAR" / "GEOMETRIC"
    string OutputKey);

internal sealed class GridPriceLevelNode(JsonElement @params) : IRuleNode
{
    public string Kind => "grid_price_level";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<GridPriceLevelParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.GridCount <= 0 || p.TopPrice <= decimal.Zero || p.BottomPrice <= decimal.Zero)
            return Task.CompletedTask;

        var prices = new List<decimal>(p.GridCount);
        var isGeometric = string.Equals(p.Mode, "GEOMETRIC", StringComparison.OrdinalIgnoreCase);

        if (isGeometric && p.BottomPrice > decimal.Zero)
        {
            var ratio = (decimal)Math.Pow((double)(p.TopPrice / p.BottomPrice), 1.0 / (p.GridCount - 1));
            var level = p.BottomPrice;
            for (var i = 0; i < p.GridCount; i++)
            {
                prices.Add(level);
                level *= ratio;
            }
        }
        else
        {
            var step = (p.TopPrice - p.BottomPrice) / (p.GridCount - 1);
            for (var i = 0; i < p.GridCount; i++)
                prices.Add(p.BottomPrice + step * i);
        }

        // 将价格水平存入 DerivedValues — 使用 outputKey_COUNT 和 outputKey_0..N-1
        state.DerivedValues[p.OutputKey + "_COUNT"] = p.GridCount;
        for (var i = 0; i < prices.Count; i++)
            state.DerivedValues[$"{p.OutputKey}_{i}"] = prices[i];

        return Task.CompletedTask;
    }
}

// ── volatility_scaling ──
internal sealed record VolatilityScalingParams(string Signal, decimal BaseValue, string OutputKey);

internal sealed class VolatilityScalingNode(JsonElement @params) : IRuleNode
{
    public string Kind => "volatility_scaling";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<VolatilityScalingParams>(@params, RuleJsonOptions.Default);
            return p?.Signal is { Length: > 0 } ? [p.Signal] : ["VOLATILITY_24H"];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<VolatilityScalingParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var sigKey = string.IsNullOrWhiteSpace(p.Signal) ? "VOLATILITY_24H" : p.Signal;
        if (!state.Signals.TryGetValue(sigKey, out var volSig) || volSig.Value <= decimal.Zero)
            return Task.CompletedTask;

        // scaled = baseValue / volatility（波动率越高，缩放越小）
        var scaled = p.BaseValue / volSig.Value;
        state.DerivedValues[p.OutputKey] = scaled;

        return Task.CompletedTask;
    }
}

// ── trailing_stop_calc ──
internal sealed record TrailingStopCalcParams(decimal TrailPercent, string OutputKey);

internal sealed class TrailingStopCalcNode(JsonElement @params) : IRuleNode
{
    public string Kind => "trailing_stop_calc";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<TrailingStopCalcParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var price = state.Context.CurrentPrice;
        var pos = state.Context.Position;

        if (pos?.HasPosition() != true) return Task.CompletedTask;

        var isLong = pos.Quantity > decimal.Zero;
        var trailOffset = price * p.TrailPercent / 100m;

        if (isLong)
            state.DerivedValues[p.OutputKey] = price - trailOffset;
        else
            state.DerivedValues[p.OutputKey] = price + trailOffset;

        return Task.CompletedTask;
    }
}

// ── kelly ──
internal sealed record KellyParams(decimal WinRate, decimal AvgWinLossRatio, string OutputKey);

internal sealed class KellyNode(JsonElement @params) : IRuleNode
{
    public string Kind => "kelly";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<KellyParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.AvgWinLossRatio <= decimal.Zero) return Task.CompletedTask;

        // f* = (p*b - q) / b  where p=WinRate, b=AvgWinLossRatio, q=1-p
        var q = 1m - p.WinRate;
        var f = (p.WinRate * p.AvgWinLossRatio - q) / p.AvgWinLossRatio;

        // 上限 25%
        if (f > 0.25m) f = 0.25m;
        if (f < 0m) f = 0m;

        state.DerivedValues[p.OutputKey] = f;

        return Task.CompletedTask;
    }
}

// ── divergence_detect ──
internal sealed record DivergenceDetectParams(string PriceSignal, string VolumeSignal, string OutputKey);

internal sealed class DivergenceDetectNode(JsonElement @params) : IRuleNode
{
    public string Kind => "divergence_detect";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<DivergenceDetectParams>(@params, RuleJsonOptions.Default);
            return p is not null ? [p.PriceSignal, p.VolumeSignal] : [];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<DivergenceDetectParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        if (!state.Signals.TryGetValue(p.PriceSignal, out var priceSig) ||
            !state.Signals.TryGetValue(p.VolumeSignal, out var volSig))
            return Task.CompletedTask;

        var priceDelta = priceSig.Value - priceSig.PrevValue;
        var volDelta = volSig.Value - volSig.PrevValue;

        // 顶背离：价格涨但量缩 → -1
        // 底背离：价格跌但量增 → +1
        decimal result = 0;
        if (priceDelta > decimal.Zero && volDelta < decimal.Zero)
            result = -1; // 顶背离
        else if (priceDelta < decimal.Zero && volDelta > decimal.Zero)
            result = 1;  // 底背离

        state.DerivedValues[p.OutputKey] = result;

        return Task.CompletedTask;
    }
}

// ── correlation_score ──
internal sealed record CorrelationScoreParams(string SignalA, string SignalB, string OutputKey);

internal sealed class CorrelationScoreNode(JsonElement @params) : IRuleNode
{
    public string Kind => "correlation_score";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<CorrelationScoreParams>(@params, RuleJsonOptions.Default);
            return p is not null ? [p.SignalA, p.SignalB] : [];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<CorrelationScoreParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        if (!state.Signals.TryGetValue(p.SignalA, out var sigA) ||
            !state.Signals.TryGetValue(p.SignalB, out var sigB))
            return Task.CompletedTask;

        // 瞬时方向相关性：两者同向 → +1，反向 → -1
        var dirA = sigA.Value >= sigA.PrevValue ? 1 : -1;
        var dirB = sigB.Value >= sigB.PrevValue ? 1 : -1;

        state.DerivedValues[p.OutputKey] = dirA == dirB ? 1m : -1m;

        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class DeriveNodesRegistration
{
    public static void RegisterDeriveNodes(this NodeRegistry reg)
    {
        reg.Register("crossover_check", new NodeDescriptor
        {
            Kind = "crossover_check", Phase = RulePhase.Derive,
            Description = "交叉检测：判断快慢信号的金叉/死叉",
            Category = "Derive", AllowDuplicate = true,
            Params = [
                new() { Name = "fastSignal", Type = "ref", Required = true, RefScope = "signal",
                    Description = "快速信号名称" },
                new() { Name = "slowSignal", Type = "ref", Required = true, RefScope = "signal",
                    Description = "慢速信号名称" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出到 DerivedValues 的键名" }
            ],
            EmitNames = ["{outputKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "EMA 快慢线交叉", ["params"] = new Dictionary<string, object> { ["fastSignal"] = "EMA_9", ["slowSignal"] = "EMA_21", ["outputKey"] = "EMA_CROSS" } }
            ]
        }, p => new CrossoverCheckNode(p));

        reg.Register("atr_stop_calc", new NodeDescriptor
        {
            Kind = "atr_stop_calc", Phase = RulePhase.Derive,
            Description = "ATR 止损/止盈计算：基于 ATR 计算多空止损止盈价",
            Category = "Derive",
            Params = [
                new() { Name = "atrSignal", Type = "ref", Required = false, Default = "ATR_14", RefScope = "signal",
                    Description = "ATR 信号名称" },
                new() { Name = "multiplier", Type = "float", Required = true, Min = 0,
                    Description = "ATR 乘数" },
                new() { Name = "longStopKey", Type = "string", Required = true,
                    Description = "多仓止损输出键" },
                new() { Name = "longTpKey", Type = "string", Required = true,
                    Description = "多仓止盈输出键" },
                new() { Name = "shortStopKey", Type = "string", Required = true,
                    Description = "空仓止损输出键" },
                new() { Name = "shortTpKey", Type = "string", Required = true,
                    Description = "空仓止盈输出键" }
            ],
            EmitNames = ["{longStopKey}", "{longTpKey}", "{shortStopKey}", "{shortTpKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "2倍 ATR 止损", ["params"] = new Dictionary<string, object> { ["atrSignal"] = "ATR_14", ["multiplier"] = 2m, ["longStopKey"] = "LONG_STOP", ["longTpKey"] = "LONG_TP", ["shortStopKey"] = "SHORT_STOP", ["shortTpKey"] = "SHORT_TP" } }
            ]
        }, p => new AtrStopCalcNode(p));

        reg.Register("grid_price_level", new NodeDescriptor
        {
            Kind = "grid_price_level", Phase = RulePhase.Derive,
            Description = "网格价格水平：计算等差/等比网格价格",
            Category = "Derive",
            Params = [
                new() { Name = "topPrice", Type = "float", Required = true, Min = 0,
                    Description = "网格上界价格", Unit = "USDT" },
                new() { Name = "bottomPrice", Type = "float", Required = true, Min = 0,
                    Description = "网格下界价格", Unit = "USDT" },
                new() { Name = "gridCount", Type = "int", Required = true, Min = 2,
                    Description = "网格数量" },
                new() { Name = "mode", Type = "string", Required = true,
                    Enum = ["LINEAR", "GEOMETRIC"], Description = "网格模式" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出前缀键名" }
            ],
            EmitNames = ["{outputKey}_COUNT", "{outputKey}_0..N"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "10 档线性网格", ["params"] = new Dictionary<string, object> { ["topPrice"] = 100m, ["bottomPrice"] = 80m, ["gridCount"] = 10, ["mode"] = "LINEAR", ["outputKey"] = "GRID" } }
            ]
        }, p => new GridPriceLevelNode(p));

        reg.Register("volatility_scaling", new NodeDescriptor
        {
            Kind = "volatility_scaling", Phase = RulePhase.Derive,
            Description = "波动率缩放：根据波动率反向缩放基准值",
            Category = "Derive", AllowDuplicate = true,
            Params = [
                new() { Name = "signal", Type = "ref", Required = false, Default = "VOLATILITY_24H", RefScope = "signal",
                    Description = "波动率信号名称" },
                new() { Name = "baseValue", Type = "float", Required = true,
                    Description = "基准值" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出键名" }
            ],
            EmitNames = ["{outputKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "基准 100 按波动率缩放", ["params"] = new Dictionary<string, object> { ["signal"] = "VOLATILITY_24H", ["baseValue"] = 100m, ["outputKey"] = "SCALED_SIZE" } }
            ]
        }, p => new VolatilityScalingNode(p));

        reg.Register("trailing_stop_calc", new NodeDescriptor
        {
            Kind = "trailing_stop_calc", Phase = RulePhase.Derive,
            Description = "追踪止损计算：根据当前价格和持仓计算追踪止损价",
            Category = "Derive",
            Params = [
                new() { Name = "trailPercent", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "追踪距离百分比", Unit = "%" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出键名" }
            ],
            EmitNames = ["{outputKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "2% 追踪止损", ["params"] = new Dictionary<string, object> { ["trailPercent"] = 2m, ["outputKey"] = "TRAIL_STOP" } }
            ]
        }, p => new TrailingStopCalcNode(p));

        reg.Register("kelly", new NodeDescriptor
        {
            Kind = "kelly", Phase = RulePhase.Derive,
            Description = "凯利公式：根据胜率和盈亏比计算最优仓位比例",
            Category = "Derive",
            Params = [
                new() { Name = "winRate", Type = "float", Required = true,
                    Min = 0, Max = 1, Description = "胜率 (0~1)" },
                new() { Name = "avgWinLossRatio", Type = "float", Required = true,
                    Min = 0, Description = "平均盈亏比" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出键名" }
            ],
            EmitNames = ["{outputKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "60% 胜率 / 1.5 盈亏比", ["params"] = new Dictionary<string, object> { ["winRate"] = 0.6m, ["avgWinLossRatio"] = 1.5m, ["outputKey"] = "KELLY_F" } }
            ]
        }, p => new KellyNode(p));

        reg.Register("divergence_detect", new NodeDescriptor
        {
            Kind = "divergence_detect", Phase = RulePhase.Derive,
            Description = "背离检测：检测价格与成交量的顶/底背离",
            Category = "Derive", AllowDuplicate = true,
            Params = [
                new() { Name = "priceSignal", Type = "ref", Required = true, RefScope = "signal",
                    Description = "价格信号名称" },
                new() { Name = "volumeSignal", Type = "ref", Required = true, RefScope = "signal",
                    Description = "成交量信号名称" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出键名 (+1=底背离, -1=顶背离)" }
            ],
            EmitNames = ["{outputKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "价格与成交量背离", ["params"] = new Dictionary<string, object> { ["priceSignal"] = "PRICE", ["volumeSignal"] = "VOLUME", ["outputKey"] = "DIVERGENCE" } }
            ]
        }, p => new DivergenceDetectNode(p));

        reg.Register("correlation_score", new NodeDescriptor
        {
            Kind = "correlation_score", Phase = RulePhase.Derive,
            Description = "相关性评分：计算两个信号的瞬时方向相关性",
            Category = "Derive", AllowDuplicate = true,
            Params = [
                new() { Name = "signalA", Type = "ref", Required = true, RefScope = "signal",
                    Description = "信号 A 名称" },
                new() { Name = "signalB", Type = "ref", Required = true, RefScope = "signal",
                    Description = "信号 B 名称" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出键名 (+1=同向, -1=反向)" }
            ],
            EmitNames = ["{outputKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "BTC 与 ETH 相关性", ["params"] = new Dictionary<string, object> { ["signalA"] = "BTC_MOMENTUM", ["signalB"] = "ETH_MOMENTUM", ["outputKey"] = "CORRELATION" } }
            ]
        }, p => new CorrelationScoreNode(p));
    }
}
