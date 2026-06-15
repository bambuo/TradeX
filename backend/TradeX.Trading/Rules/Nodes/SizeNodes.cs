using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

// ═══════════════════════════════════════════════════════════════════
// Size 阶段节点 — 仓位计算，产出 SizeDecision
// ═══════════════════════════════════════════════════════════════════

// ── fixed_size ──
internal sealed record FixedSizeParams(decimal Amount, string Currency);

internal sealed class FixedSizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "fixed_size";
    public RulePhase Phase => RulePhase.Size;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<FixedSizeParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.Amount <= decimal.Zero) return Task.CompletedTask;

        state.SizeDecisions.Add(new SizeDecision
        {
            Intent = "ENTER",
            Amount = p.Amount,
            Currency = string.IsNullOrWhiteSpace(p.Currency) ? "USDT" : p.Currency,
            Reason = "fixed_size"
        });

        return Task.CompletedTask;
    }
}

// ── pyramiding_size ──
internal sealed record PyramidingSizeParams(decimal BaseAmount, decimal Multiplier, int MaxLevel);

internal sealed class PyramidingSizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "pyramiding_size";
    public RulePhase Phase => RulePhase.Size;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PyramidingSizeParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.BaseAmount <= decimal.Zero) return Task.CompletedTask;

        var currentLevel = state.Context.Position?.LotCount ?? 0;
        if (currentLevel >= p.MaxLevel) return Task.CompletedTask;

        // baseAmount * multiplier^level
        var amount = p.BaseAmount * (decimal)Math.Pow((double)p.Multiplier, currentLevel);

        state.SizeDecisions.Add(new SizeDecision
        {
            Intent = "ENTER",
            Amount = amount,
            Currency = "USDT",
            Reason = $"pyramiding_size:L{currentLevel}"
        });

        return Task.CompletedTask;
    }
}

// ── account_ratio_size ──
internal sealed record AccountRatioSizeParams(decimal Ratio);

internal sealed class AccountRatioSizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "account_ratio_size";
    public RulePhase Phase => RulePhase.Size;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<AccountRatioSizeParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.Ratio <= decimal.Zero || p.Ratio > 1m) return Task.CompletedTask;

        var equity = state.Context.Portfolio?.TotalEquity ?? decimal.Zero;
        var amount = equity * p.Ratio;

        state.SizeDecisions.Add(new SizeDecision
        {
            Intent = "ENTER",
            Amount = amount,
            Currency = "USDT",
            Reason = $"account_ratio_size:{p.Ratio:P0}"
        });

        return Task.CompletedTask;
    }
}

// ── volatility_adjusted_size ──
internal sealed record VolatilityAdjustedSizeParams(
    decimal BaseSize,
    string VolSignal,
    decimal ReferenceVol,
    string OutputKey);

internal sealed class VolatilityAdjustedSizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "volatility_adjusted_size";
    public RulePhase Phase => RulePhase.Size;
    public IReadOnlyList<string> Deps
    {
        get
        {
            var p = JsonSerializer.Deserialize<VolatilityAdjustedSizeParams>(@params, RuleJsonOptions.Default);
            return p?.VolSignal is { Length: > 0 } ? [p.VolSignal] : ["VOLATILITY_24H"];
        }
    }

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<VolatilityAdjustedSizeParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var sigKey = string.IsNullOrWhiteSpace(p.VolSignal) ? "VOLATILITY_24H" : p.VolSignal;
        if (!state.Signals.TryGetValue(sigKey, out var volSig) || volSig.Value <= decimal.Zero)
            return Task.CompletedTask;

        var refVol = p.ReferenceVol > decimal.Zero ? p.ReferenceVol : volSig.Value;
        // 波动率越高仓位越小：baseSize * (referenceVol / currentVol)
        var adjusted = p.BaseSize * refVol / volSig.Value;

        state.DerivedValues[p.OutputKey] = adjusted;

        state.SizeDecisions.Add(new SizeDecision
        {
            Intent = "ENTER",
            Amount = adjusted,
            Currency = "USDT",
            Reason = $"volatility_adjusted_size:vol={volSig.Value}"
        });

        return Task.CompletedTask;
    }
}

// ── grid_size ──
internal sealed record GridSizeParams(decimal TotalAmount, int GridCount, string Mode); // LINEAR / GEOMETRIC

internal sealed class GridSizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "grid_size";
    public RulePhase Phase => RulePhase.Size;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<GridSizeParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.GridCount <= 0 || p.TotalAmount <= decimal.Zero) return Task.CompletedTask;

        var isGeometric = string.Equals(p.Mode, "GEOMETRIC", StringComparison.OrdinalIgnoreCase);

        if (isGeometric)
        {
            // 等比分配：等比例增加
            var totalRatio = (decimal)((1 - Math.Pow(2, p.GridCount)) / (1 - 2)); // sum of 2^0..2^(N-1)
            for (var i = 0; i < p.GridCount; i++)
            {
                var weight = (decimal)Math.Pow(2, i) / totalRatio;
                state.SizeDecisions.Add(new SizeDecision
                {
                    Intent = "ENTER",
                    Amount = p.TotalAmount * weight,
                    Currency = "USDT",
                    Reason = $"grid_size:GEOMETRIC:L{i}"
                });
            }
        }
        else
        {
            // 等差分配
            var perGrid = p.TotalAmount / p.GridCount;
            for (var i = 0; i < p.GridCount; i++)
            {
                state.SizeDecisions.Add(new SizeDecision
                {
                    Intent = "ENTER",
                    Amount = perGrid,
                    Currency = "USDT",
                    Reason = $"grid_size:LINEAR:L{i}"
                });
            }
        }

        return Task.CompletedTask;
    }
}

// ── kelly_size ──
internal sealed record KellySizeParams(decimal KellyFraction);

internal sealed class KellySizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "kelly_size";
    public RulePhase Phase => RulePhase.Size;
    public IReadOnlyList<string> Deps => ["KELLY_F"];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<KellySizeParams>(@params, RuleJsonOptions.Default);
        if (p is null) return Task.CompletedTask;

        var kellyF = decimal.Zero;
        if (state.DerivedValues.TryGetValue("KELLY_F", out var dv))
            kellyF = dv;
        else if (state.Signals.TryGetValue("KELLY_F", out var sig))
            kellyF = sig.Value;

        if (kellyF <= decimal.Zero) return Task.CompletedTask;

        var equity = state.Context.Portfolio?.TotalEquity ?? decimal.Zero;
        var amount = equity * kellyF * p.KellyFraction;

        state.SizeDecisions.Add(new SizeDecision
        {
            Intent = "ENTER",
            Amount = amount,
            Currency = "USDT",
            Reason = $"kelly_size:f*={kellyF:P2}"
        });

        return Task.CompletedTask;
    }
}

// ── portfolio_alloc_size ──
internal sealed record PortfolioAllocSizeParams(decimal AllocationPercent);

internal sealed class PortfolioAllocSizeNode(JsonElement @params) : IRuleNode
{
    public string Kind => "portfolio_alloc_size";
    public RulePhase Phase => RulePhase.Size;
    public IReadOnlyList<string> Deps => [];

    public Task ProcessAsync(ChainState state, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PortfolioAllocSizeParams>(@params, RuleJsonOptions.Default);
        if (p is null || p.AllocationPercent <= decimal.Zero || p.AllocationPercent > 100m)
            return Task.CompletedTask;

        var equity = state.Context.Portfolio?.TotalEquity ?? decimal.Zero;
        var amount = equity * p.AllocationPercent / 100m;

        state.SizeDecisions.Add(new SizeDecision
        {
            Intent = "ENTER",
            Amount = amount,
            Currency = "USDT",
            Reason = $"portfolio_alloc_size:{p.AllocationPercent}%"
        });

        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════
// 注册扩展
// ═══════════════════════════════════════════════════════════════════

public static class SizeNodesRegistration
{
    public static void RegisterSizeNodes(this NodeRegistry reg)
    {
        reg.Register("fixed_size", new NodeDescriptor
        {
            Kind = "fixed_size", Phase = RulePhase.Size,
            Description = "固定仓位：每次开仓固定金额",
            Category = "Size", ProducesDecisions = true,
            Params = [
                new() { Name = "amount", Type = "float", Required = true,
                    Min = 0, Description = "开仓金额", Unit = "USDT" },
                new() { Name = "currency", Type = "string", Required = false, Default = "USDT",
                    Description = "计价币种" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "每次 50 USDT", ["params"] = new Dictionary<string, object> { ["amount"] = 50m, ["currency"] = "USDT" } }
            ]
        }, p => new FixedSizeNode(p));

        reg.Register("pyramiding_size", new NodeDescriptor
        {
            Kind = "pyramiding_size", Phase = RulePhase.Size,
            Description = "金字塔加仓：按层级指数级调整仓位",
            Category = "Size", ProducesDecisions = true,
            Params = [
                new() { Name = "baseAmount", Type = "float", Required = true,
                    Min = 0, Description = "基础金额", Unit = "USDT" },
                new() { Name = "multiplier", Type = "float", Required = true,
                    Min = 0, Description = "层级乘数" },
                new() { Name = "maxLevel", Type = "int", Required = true,
                    Min = 1, Description = "最大加仓层数" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "3 层金字塔", ["params"] = new Dictionary<string, object> { ["baseAmount"] = 100m, ["multiplier"] = 0.5m, ["maxLevel"] = 3 } }
            ]
        }, p => new PyramidingSizeNode(p));

        reg.Register("account_ratio_size", new NodeDescriptor
        {
            Kind = "account_ratio_size", Phase = RulePhase.Size,
            Description = "账户比例仓位：按账户总权益的固定比例计算仓位",
            Category = "Size", ProducesDecisions = true,
            Params = [
                new() { Name = "ratio", Type = "float", Required = true,
                    Min = 0, Max = 1, Description = "账户权益比例 (0~1)" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "账户 10%", ["params"] = new Dictionary<string, object> { ["ratio"] = 0.1m } }
            ]
        }, p => new AccountRatioSizeNode(p));

        reg.Register("volatility_adjusted_size", new NodeDescriptor
        {
            Kind = "volatility_adjusted_size", Phase = RulePhase.Size,
            Description = "波动率调整仓位：波动率越高仓位越小",
            Category = "Size", ProducesDecisions = true,
            Params = [
                new() { Name = "baseSize", Type = "float", Required = true,
                    Min = 0, Description = "基准仓位金额", Unit = "USDT" },
                new() { Name = "volSignal", Type = "ref", Required = false, Default = "VOLATILITY_24H", RefScope = "signal",
                    Description = "波动率信号名称" },
                new() { Name = "referenceVol", Type = "float", Required = false,
                    Min = 0, Description = "参考波动率 (留空用当前值)" },
                new() { Name = "outputKey", Type = "string", Required = true,
                    Description = "输出键名" }
            ],
            EmitNames = ["{outputKey}"], EmitScope = "chain",
            Examples = [
                new Dictionary<string, object> { ["title"] = "基准 100 按波动率调整", ["params"] = new Dictionary<string, object> { ["baseSize"] = 100m, ["volSignal"] = "VOLATILITY_24H", ["outputKey"] = "ADJ_SIZE" } }
            ]
        }, p => new VolatilityAdjustedSizeNode(p));

        reg.Register("grid_size", new NodeDescriptor
        {
            Kind = "grid_size", Phase = RulePhase.Size,
            Description = "网格仓位：按等差/等比分配每格资金",
            Category = "Size", ProducesDecisions = true,
            Params = [
                new() { Name = "totalAmount", Type = "float", Required = true,
                    Min = 0, Description = "总资金", Unit = "USDT" },
                new() { Name = "gridCount", Type = "int", Required = true,
                    Min = 1, Description = "网格数量" },
                new() { Name = "mode", Type = "string", Required = true,
                    Enum = ["LINEAR", "GEOMETRIC"], Description = "分配模式" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "10 格线性分配 1000 USDT", ["params"] = new Dictionary<string, object> { ["totalAmount"] = 1000m, ["gridCount"] = 10, ["mode"] = "LINEAR" } }
            ]
        }, p => new GridSizeNode(p));

        reg.Register("kelly_size", new NodeDescriptor
        {
            Kind = "kelly_size", Phase = RulePhase.Size,
            Description = "凯利仓位：根据凯利公式计算仓位金额",
            Category = "Size", ProducesDecisions = true,
            Params = [
                new() { Name = "kellyFraction", Type = "float", Required = true,
                    Min = 0, Max = 1, Description = "凯利比例系数 (0~1)" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "半凯利", ["params"] = new Dictionary<string, object> { ["kellyFraction"] = 0.5m } }
            ]
        }, p => new KellySizeNode(p));

        reg.Register("portfolio_alloc_size", new NodeDescriptor
        {
            Kind = "portfolio_alloc_size", Phase = RulePhase.Size,
            Description = "组合分配仓位：按账户权益百分比计算仓位",
            Category = "Size", ProducesDecisions = true,
            Params = [
                new() { Name = "allocationPercent", Type = "float", Required = true,
                    Min = 0, Max = 100, Description = "分配百分比", Unit = "%" }
            ],
            Examples = [
                new Dictionary<string, object> { ["title"] = "5% 仓位", ["params"] = new Dictionary<string, object> { ["allocationPercent"] = 5m } }
            ]
        }, p => new PortfolioAllocSizeNode(p));
    }
}
