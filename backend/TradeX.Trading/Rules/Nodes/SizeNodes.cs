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
        reg.Register("fixed_size", p => new FixedSizeNode(p));
        reg.Register("pyramiding_size", p => new PyramidingSizeNode(p));
        reg.Register("account_ratio_size", p => new AccountRatioSizeNode(p));
        reg.Register("volatility_adjusted_size", p => new VolatilityAdjustedSizeNode(p));
        reg.Register("grid_size", p => new GridSizeNode(p));
        reg.Register("kelly_size", p => new KellySizeNode(p));
        reg.Register("portfolio_alloc_size", p => new PortfolioAllocSizeNode(p));
    }
}
