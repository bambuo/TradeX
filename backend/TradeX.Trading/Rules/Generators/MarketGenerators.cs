using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Generators;

// ──────────────────────────────────────────────────────────────
// FundingRate
// ──────────────────────────────────────────────────────────────

public sealed class FundingRateGenerator : ISignalGenerator
{
    public string Name => "FUNDING_RATE_8H";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var ts = ctx.KlineWindow.Count > 0 ? ctx.KlineWindow[^1].Timestamp : DateTime.UtcNow;

        if (ctx.FundingRate is null)
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new()
                {
                    Name = Name,
                    Value = 0m,
                    Quality = SignalQuality.Stale,
                    Meta = new Dictionary<string, string> { ["note"] = "no funding rate" },
                },
            });
        }

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = ctx.FundingRate.Value, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// Volatility24h
// ──────────────────────────────────────────────────────────────

public sealed class Volatility24hGenerator(int windowSize) : ISignalGenerator
{
    public string Name => "VOLATILITY_24H";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < windowSize)
            throw new InvalidOperationException($"Volatility24h: need {windowSize} klines, got {klines.Count}");

        var start = klines.Count - windowSize;
        var closes = new double[windowSize];
        for (var i = 0; i < windowSize; i++)
            closes[i] = (double)klines[start + i].Close;

        // 计算对数收益率的标准差
        var returns = new double[closes.Length - 1];
        for (var i = 1; i < closes.Length; i++)
        {
            if (closes[i - 1] > 0)
                returns[i - 1] = Math.Log(closes[i] / closes[i - 1]);
        }

        var mean = 0.0;
        foreach (var r in returns) mean += r;
        mean /= returns.Length;

        var variance = 0.0;
        foreach (var r in returns)
        {
            var d = r - mean;
            variance += d * d;
        }
        variance /= returns.Length;

        var vol = Math.Sqrt(variance) * 100; // 百分比
        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = (decimal)vol, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// VolumeSpike (成交量突变)
// ──────────────────────────────────────────────────────────────

public sealed class VolumeSpikeGenerator(int period) : ISignalGenerator
{
    public string Name => "VOLUME_RATIO";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < period + 1)
            throw new InvalidOperationException($"VolumeSpike: need {period + 1} klines, got {klines.Count}");

        var currentVol = klines[^1].Volume;
        var avgVol = 0m;
        var start = klines.Count - period - 1;
        for (var i = start; i < klines.Count - 1; i++)
            avgVol += klines[i].Volume;
        avgVol /= period;

        var ratio = avgVol > 0 ? currentVol / avgVol : 1m;
        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = ratio, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// BidAskRatio
// ──────────────────────────────────────────────────────────────

public sealed class BidAskRatioGenerator : ISignalGenerator
{
    public string Name => "BID_ASK_RATIO";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var ts = ctx.KlineWindow.Count > 0 ? ctx.KlineWindow[^1].Timestamp : DateTime.UtcNow;

        // 当前 SignalContext 没有 OrderBookSnapshot，产出中性值
        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new()
            {
                Name = Name,
                Value = 1m,
                Quality = SignalQuality.Stale,
                Meta = new Dictionary<string, string> { ["note"] = "no orderbook" },
            },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// LiquidityDepth
// ──────────────────────────────────────────────────────────────

public sealed class LiquidityDepthGenerator(double rangePct) : ISignalGenerator
{
    private readonly double _rangePct = rangePct > 0 ? rangePct : 2.0;

    public string Name => "LIQUIDITY_DEPTH_PERCENT";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var ts = ctx.KlineWindow.Count > 0 ? ctx.KlineWindow[^1].Timestamp : DateTime.UtcNow;

        // 当前 SignalContext 没有 OrderBookSnapshot，产出 0
        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = 0m, Quality = SignalQuality.Stale },
        });
    }
}
