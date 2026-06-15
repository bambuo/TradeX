using TradeX.Core.Enums;
using TradeX.Core.Models;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Generators;

// ──────────────────────────────────────────────────────────────
// DeviationFromAvg (持仓偏离度)
// ──────────────────────────────────────────────────────────────

public sealed class DeviationFromAvgGenerator : ISignalGenerator
{
    public string Name => "DEVIATION_FROM_AVG";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;

        if (ctx.Position is null || ctx.Position.EntryPrice == 0)
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new()
                {
                    Name = Name,
                    Value = 0m,
                    Quality = SignalQuality.Normal,
                    Meta = new Dictionary<string, string> { ["note"] = "no position" },
                },
            });
        }

        var deviation = (ctx.CurrentPrice - ctx.Position.EntryPrice) / ctx.Position.EntryPrice * 100;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = deviation, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// PyramidingLevel (加仓层数)
// ──────────────────────────────────────────────────────────────

public sealed class PyramidingLevelGenerator : ISignalGenerator
{
    public string Name => "PYRAMIDING_LEVEL";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;
        var level = ctx.Position?.LotCount ?? 0;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = level, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// PositionNotional (持仓名义价值)
// ──────────────────────────────────────────────────────────────

public sealed class PositionNotionalGenerator : ISignalGenerator
{
    public string Name => "POSITION_NOTIONAL";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;

        if (ctx.Position is null)
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = 0m, Quality = SignalQuality.Normal },
            });
        }

        var notional = Math.Abs(ctx.Position.Quantity) * ctx.CurrentPrice;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = notional, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// PositionPnlPct (持仓盈亏百分比)
// ──────────────────────────────────────────────────────────────

public sealed class PositionPnlPctGenerator : ISignalGenerator
{
    public string Name => "POSITION_PNL_PCT";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;

        if (ctx.Position is null || ctx.Position.EntryPrice == 0)
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = 0m, Quality = SignalQuality.Normal },
            });
        }

        var pnlPct = (ctx.CurrentPrice - ctx.Position.EntryPrice) / ctx.Position.EntryPrice * 100;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = pnlPct, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// PositionCount (持仓笔数)
// ──────────────────────────────────────────────────────────────

public sealed class PositionCountGenerator : ISignalGenerator
{
    public string Name => "POSITION_COUNT";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;
        var count = ctx.Position?.LotCount ?? 0;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = count, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// PortfolioDrawdown (组合回撤百分比)
// ──────────────────────────────────────────────────────────────

public sealed class PortfolioDrawdownGenerator : ISignalGenerator
{
    public string Name => "PORTFOLIO_DRAWDOWN";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;

        if (ctx.Portfolio is null)
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = 0m, Quality = SignalQuality.Normal },
            });
        }

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = ctx.Portfolio.Drawdown, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// AvailableCash (可用资金)
// ──────────────────────────────────────────────────────────────

public sealed class AvailableCashGenerator : ISignalGenerator
{
    public string Name => "AVAILABLE_CASH";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;

        if (ctx.Portfolio is null)
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = 0m, Quality = SignalQuality.Normal },
            });
        }

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = ctx.Portfolio.AvailableCash, Quality = SignalQuality.High },
        });
    }
}
