using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Generators;

/// <summary>
/// 市场体制枚举。下游禁止做大小比较（§10.8），只能做等值判断。
/// 0=RANGING 震荡, 1=TRENDING 趋势, 2=HIGH_VOL 高波动, 3=CRASH 崩盘, 4=LOW_VOL 低波动
/// </summary>
public enum RegimeEnum
{
    Ranging = 0,
    Trending = 1,
    HighVol = 2,
    Crash = 3,
    LowVol = 4,
}

/// <summary>
/// 市场体制检测生成器。
/// 依赖 ADX 和 Volatility 信号进行分类。
/// 输出是分类枚举（0-4），下游禁止做大小比较（§10.8）。
/// </summary>
public sealed class RegimeDetectorGenerator(
    double highVolThreshold,
    double lowVolThreshold,
    double adxTrendThreshold,
    double crashDropThreshold) : ISignalGenerator
{
    public string Name => "MARKET_REGIME";
    public string[] Deps => ["ADX_14", "VOLATILITY_24H"];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;

        // 从 PrevSignals 获取依赖信号
        var hasADX = ctx.PrevSignals.TryGetValue("ADX_14", out var adxSig);
        var hasVol = ctx.PrevSignals.TryGetValue("VOLATILITY_24H", out var volSig);

        if (!hasADX || !hasVol)
        {
            // 依赖信号缺失，降级为 RANGING + QualityStale
            return Task.FromResult(new Dictionary<string, Signal>
            {
                [Name] = new()
                {
                    Name = Name,
                    Value = (int)RegimeEnum.Ranging,
                    Quality = SignalQuality.Stale,
                    Meta = new Dictionary<string, string>
                    {
                        ["regime"] = RegimeEnum.Ranging.ToString(),
                        ["note"] = "missing deps",
                    },
                },
            });
        }

        var adx = (double)adxSig!.Value;
        var vol = (double)volSig!.Value;

        var regime = RegimeEnum.Ranging;

        // 检查崩盘：最近一根 K 线跌幅超过阈值
        if (klines.Count >= 2)
        {
            var lastClose = (double)klines[^1].Close;
            var prevClose = (double)klines[^2].Close;
            if (prevClose > 0)
            {
                var dropPct = (prevClose - lastClose) / prevClose * 100;
                if (dropPct > crashDropThreshold)
                    regime = RegimeEnum.Crash;
            }
        }

        // 非崩盘场景按 ADX + Vol 分类
        if (regime != RegimeEnum.Crash)
        {
            if (vol >= highVolThreshold)
                regime = RegimeEnum.HighVol;
            else if (vol < lowVolThreshold)
                regime = RegimeEnum.LowVol;
            else if (adx >= adxTrendThreshold)
                regime = RegimeEnum.Trending;
            else
                regime = RegimeEnum.Ranging;
        }

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new()
            {
                Name = Name,
                Value = (int)regime,
                Quality = SignalQuality.Normal,
                Meta = new Dictionary<string, string> { ["regime"] = regime.ToString() },
            },
        });
    }
}
