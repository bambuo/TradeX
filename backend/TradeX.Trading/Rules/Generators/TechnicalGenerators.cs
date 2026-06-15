using TradeX.Core.Enums;
using TradeX.Core.Models;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Generators;

// ──────────────────────────────────────────────────────────────
// SMA
// ──────────────────────────────────────────────────────────────

public sealed class SMAGenerator(int period) : ISignalGenerator
{
    public string Name => $"SMA_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < period)
            throw new InvalidOperationException($"SMA: need {period} klines, got {klines.Count}");

        var start = klines.Count - period;
        var sum = 0m;
        for (var i = start; i < klines.Count; i++)
            sum += klines[i].Close;

        var val = sum / period;
        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = val, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// EMA
// ──────────────────────────────────────────────────────────────

public sealed class EMAGenerator(int period) : ISignalGenerator
{
    public string Name => $"EMA_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < period)
            throw new InvalidOperationException($"EMA: need {period} klines, got {klines.Count}");

        // 初始 EMA = SMA(period)
        var sum = 0m;
        for (var i = 0; i < period; i++)
            sum += klines[i].Close;
        var ema = sum / period;

        var k = 2m / (period + 1);
        var oneMinusK = 1m - k;

        for (var i = period; i < klines.Count; i++)
            ema = klines[i].Close * k + ema * oneMinusK;

        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = ema, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// RSI
// ──────────────────────────────────────────────────────────────

public sealed class RSIGenerator(int period) : ISignalGenerator
{
    public string Name => $"RSI_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < period + 1)
            throw new InvalidOperationException($"RSI: need {period + 1} klines, got {klines.Count}");

        // 计算平均涨幅和平均跌幅（Wilder 平滑法）
        var avgGain = 0m;
        var avgLoss = 0m;
        for (var i = 1; i <= period; i++)
        {
            var change = klines[i].Close - klines[i - 1].Close;
            if (change > 0)
                avgGain += change;
            else
                avgLoss += Math.Abs(change);
        }
        avgGain /= period;
        avgLoss /= period;

        // Wilder 平滑
        for (var i = period + 1; i < klines.Count; i++)
        {
            var change = klines[i].Close - klines[i - 1].Close;
            var gain = change > 0 ? change : 0;
            var loss = change < 0 ? Math.Abs(change) : 0;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        // RSI = 100 - 100 / (1 + RS)
        var rsi = 100m;
        if (avgLoss > 0)
        {
            var rs = avgGain / avgLoss;
            rsi = 100m - 100m / (1 + rs);
        }

        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = rsi, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// MACD (主信号: MACD line), MACD_SIGNAL, MACD_HIST
// ──────────────────────────────────────────────────────────────

public sealed class MACDGenerator(int fast, int slow, int signalPeriod) : ISignalGenerator
{
    public string Name => "MACD";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < slow + signalPeriod)
            throw new InvalidOperationException($"MACD: need {slow + signalPeriod} klines, got {klines.Count}");

        var closes = klines.Select(k => (double)k.Close).ToArray();
        var fastEma = CalcEMA(closes, fast);
        var slowEma = CalcEMA(closes, slow);

        // MACD Line = Fast EMA - Slow EMA
        var macdLine = new double[fastEma.Length];
        for (var i = 0; i < fastEma.Length; i++)
            macdLine[i] = fastEma[i] - slowEma[i];

        // Signal Line = EMA(MACD Line, signal)
        var signalLine = CalcEMA(macdLine, signalPeriod);

        var macd = (decimal)macdLine[^1];
        var sig = (decimal)signalLine[^1];
        var hist = macd - sig;

        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new()
            {
                Name = Name,
                Value = macd,
                Quality = SignalQuality.High,
                Meta = new Dictionary<string, string>
                {
                    ["signal"] = sig.ToString("F8"),
                    ["hist"] = hist.ToString("F8"),
                },
            },
        });
    }

    private static double[] CalcEMA(double[] data, int period)
    {
        if (data.Length < period) return [];
        var k = 2.0 / (period + 1);
        var result = new double[data.Length];

        // 初始 SMA
        var sum = 0.0;
        for (var i = 0; i < period; i++) sum += data[i];
        result[period - 1] = sum / period;

        for (var i = period; i < data.Length; i++)
            result[i] = data[i] * k + result[i - 1] * (1 - k);

        return result;
    }
}

public sealed class MACDSignalGenerator : ISignalGenerator
{
    public string Name => "MACD_SIGNAL";
    public string[] Deps => ["MACD"];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        if (ctx.PrevSignals.TryGetValue("MACD", out var macdSig) &&
            macdSig.Meta?.TryGetValue("signal", out var sigStr) == true &&
            decimal.TryParse(sigStr, out var val))
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = val, Quality = SignalQuality.High },
            });
        }

        throw new InvalidOperationException("MACD_SIGNAL: MACD signal not found in prev signals");
    }
}

public sealed class MACDHistogramGenerator : ISignalGenerator
{
    public string Name => "MACD_HIST";
    public string[] Deps => ["MACD"];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        if (ctx.PrevSignals.TryGetValue("MACD", out var macdSig) &&
            macdSig.Meta?.TryGetValue("hist", out var histStr) == true &&
            decimal.TryParse(histStr, out var val))
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = val, Quality = SignalQuality.High },
            });
        }

        throw new InvalidOperationException("MACD_HIST: MACD histogram not found in prev signals");
    }
}

// ──────────────────────────────────────────────────────────────
// Bollinger Bands (中轨、上轨、下轨)
// ──────────────────────────────────────────────────────────────

public sealed class BollingerGenerator(int period, double k) : ISignalGenerator
{
    public string Name => $"BB_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var (mid, upper, lower, ts) = CalcBollinger(ctx, period, (decimal)k);
        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new()
            {
                Name = Name,
                Value = mid,
                Quality = SignalQuality.High,
                Meta = new Dictionary<string, string>
                {
                    ["upper"] = upper.ToString("F8"),
                    ["lower"] = lower.ToString("F8"),
                },
            },
        });
    }

    internal static (decimal Mid, decimal Upper, decimal Lower, DateTime Ts) CalcBollinger(
        SignalContext ctx, int period, decimal k)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < period)
            throw new InvalidOperationException($"BB: need {period} klines, got {klines.Count}");

        var start = klines.Count - period;
        var sum = 0m;
        for (var i = start; i < klines.Count; i++)
            sum += klines[i].Close;

        var n = (decimal)period;
        var mid = sum / n;

        var variance = 0m;
        for (var i = start; i < klines.Count; i++)
        {
            var diff = klines[i].Close - mid;
            variance += diff * diff;
        }
        variance /= n;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        var upper = mid + k * stdDev;
        var lower = mid - k * stdDev;
        var ts = klines[^1].Timestamp;

        return (mid, upper, lower, ts);
    }
}

public sealed class BollingerUpperGenerator(int period, double k) : ISignalGenerator
{
    public string Name => $"BB_UPPER_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var (_, upper, _, ts) = BollingerGenerator.CalcBollinger(ctx, period, (decimal)k);
        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = upper, Quality = SignalQuality.High },
        });
    }
}

public sealed class BollingerLowerGenerator(int period, double k) : ISignalGenerator
{
    public string Name => $"BB_LOWER_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var (_, _, lower, ts) = BollingerGenerator.CalcBollinger(ctx, period, (decimal)k);
        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = lower, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// ATR
// ──────────────────────────────────────────────────────────────

public sealed class ATRGenerator(int period) : ISignalGenerator
{
    public string Name => $"ATR_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < period + 1)
            throw new InvalidOperationException($"ATR: need {period + 1} klines, got {klines.Count}");

        // True Range = max(H-L, |H-PrevC|, |L-PrevC|)
        var trs = new List<decimal>(klines.Count - 1);
        for (var i = 1; i < klines.Count; i++)
        {
            var hl = klines[i].High - klines[i].Low;
            var hc = Math.Abs(klines[i].High - klines[i - 1].Close);
            var lc = Math.Abs(klines[i].Low - klines[i - 1].Close);
            var tr = Math.Max(hl, Math.Max(hc, lc));
            trs.Add(tr);
        }

        // 初始 ATR = SMA(TR, period)
        var atr = 0m;
        for (var i = 0; i < period; i++)
            atr += trs[i];
        var n = (decimal)period;
        atr /= n;

        // Wilder 平滑
        for (var i = period; i < trs.Count; i++)
            atr = (atr * (n - 1) + trs[i]) / n;

        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = atr, Quality = SignalQuality.High },
        });
    }
}

// ──────────────────────────────────────────────────────────────
// ADX
// ──────────────────────────────────────────────────────────────

public sealed class ADXGenerator(int period) : ISignalGenerator
{
    public string Name => $"ADX_{period}";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < period * 2 + 1)
            throw new InvalidOperationException($"ADX: need {period * 2 + 1} klines, got {klines.Count}");

        var len = klines.Count;

        // 计算 +DM 和 -DM 和 TR
        var plusDM = new double[len - 1];
        var minusDM = new double[len - 1];
        var tr = new double[len - 1];

        for (var i = 1; i < len; i++)
        {
            var h = (double)klines[i].High;
            var l = (double)klines[i].Low;
            var prevH = (double)klines[i - 1].High;
            var prevL = (double)klines[i - 1].Low;
            var prevC = (double)klines[i - 1].Close;

            var upMove = h - prevH;
            var downMove = prevL - l;

            if (upMove > downMove && upMove > 0)
                plusDM[i - 1] = upMove;
            if (downMove > upMove && downMove > 0)
                minusDM[i - 1] = downMove;

            var hl = h - l;
            var hc = Math.Abs(h - prevC);
            var lc = Math.Abs(l - prevC);
            tr[i - 1] = Math.Max(hl, Math.Max(hc, lc));
        }

        var smoothTR = SmoothWilder(tr, period);
        var smoothPlusDM = SmoothWilder(plusDM, period);
        var smoothMinusDM = SmoothWilder(minusDM, period);

        // DX
        var dx = new List<double>();
        for (var i = period; i < smoothTR.Length; i++)
        {
            if (smoothTR[i] == 0)
            {
                dx.Add(0);
                continue;
            }
            var plusDI = 100 * smoothPlusDM[i] / smoothTR[i];
            var minusDI = 100 * smoothMinusDM[i] / smoothTR[i];
            var diSum = plusDI + minusDI;
            dx.Add(diSum == 0 ? 0 : 100 * Math.Abs(plusDI - minusDI) / diSum);
        }

        if (dx.Count < period)
            throw new InvalidOperationException("ADX: insufficient data for smoothing");

        // ADX = SMA(DX, period)
        var adx = 0.0;
        for (var i = 0; i < period; i++)
            adx += dx[i];
        adx /= period;

        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = (decimal)adx, Quality = SignalQuality.High },
        });
    }

    private static double[] SmoothWilder(double[] data, int period)
    {
        var result = new double[data.Length];
        var sum = 0.0;
        for (var i = 0; i < period; i++) sum += data[i];
        result[period - 1] = sum;
        for (var i = period; i < data.Length; i++)
            result[i] = result[i - 1] - result[i - 1] / period + data[i];
        return result;
    }
}

// ──────────────────────────────────────────────────────────────
// Stochastic (KDJ)
// ──────────────────────────────────────────────────────────────

public sealed class StochasticGenerator(int kPeriod, int dPeriod) : ISignalGenerator
{
    public string Name => "STOCH";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        if (klines.Count < kPeriod + dPeriod)
            throw new InvalidOperationException($"Stochastic: need {kPeriod + dPeriod} klines, got {klines.Count}");

        // 计算 %K 序列
        var kValues = new List<double>();
        for (var i = kPeriod - 1; i < klines.Count; i++)
        {
            var highest = klines[i].High;
            var lowest = klines[i].Low;
            for (var j = i - kPeriod + 1; j <= i; j++)
            {
                if (klines[j].High > highest) highest = klines[j].High;
                if (klines[j].Low < lowest) lowest = klines[j].Low;
            }

            var hl = highest - lowest;
            if (hl == 0)
                kValues.Add(50.0);
            else
                kValues.Add((double)((klines[i].Close - lowest) / hl * 100));
        }

        // %D = SMA(%K, dPeriod)
        if (kValues.Count < dPeriod)
            throw new InvalidOperationException("Stochastic: insufficient K values for D");

        var dSum = 0.0;
        for (var i = kValues.Count - dPeriod; i < kValues.Count; i++)
            dSum += kValues[i];
        var d = dSum / dPeriod;
        var k = kValues[^1];

        var ts = klines[^1].Timestamp;

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new()
            {
                Name = Name,
                Value = (decimal)k,
                Quality = SignalQuality.High,
                Meta = new Dictionary<string, string>
                {
                    ["k"] = k.ToString("F4"),
                    ["d"] = d.ToString("F4"),
                },
            },
        });
    }
}

public sealed class StochKGenerator : ISignalGenerator
{
    public string Name => "STOCH_K";
    public string[] Deps => ["STOCH"];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        if (ctx.PrevSignals.TryGetValue("STOCH", out var stochSig) &&
            stochSig.Meta?.TryGetValue("k", out var kStr) == true &&
            decimal.TryParse(kStr, out var val))
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = val, Quality = SignalQuality.High },
            });
        }

        throw new InvalidOperationException("STOCH_K: STOCH k not found in prev signals");
    }
}

public sealed class StochDGenerator : ISignalGenerator
{
    public string Name => "STOCH_D";
    public string[] Deps => ["STOCH"];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        if (ctx.PrevSignals.TryGetValue("STOCH", out var stochSig) &&
            stochSig.Meta?.TryGetValue("d", out var dStr) == true &&
            decimal.TryParse(dStr, out var val))
        {
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new() { Name = Name, Value = val, Quality = SignalQuality.High },
            });
        }

        throw new InvalidOperationException("STOCH_D: STOCH d not found in prev signals");
    }
}
