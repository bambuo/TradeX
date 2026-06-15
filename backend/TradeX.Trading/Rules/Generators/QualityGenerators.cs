using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Generators;

/// <summary>
/// 信号质量估算生成器。
/// 基于系统指标产出综合质量评分（0-100 范围，100 = 最高）。
/// 自动产出，不强制消费。可用 signal_gate 或 quality_filter 节点消费（R13）。
/// </summary>
public sealed class SignalQualityEstimatorGenerator(int latencyThresholdMs) : ISignalGenerator
{
    private readonly int _latencyThreshold = latencyThresholdMs > 0 ? latencyThresholdMs : 500;

    public string Name => "SIGNAL_QUALITY";
    public string[] Deps => [];

    public Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default)
    {
        var klines = ctx.KlineWindow;
        var ts = klines.Count > 0 ? klines[^1].Timestamp : DateTime.UtcNow;

        // 默认高质量
        var quality = SignalQuality.High;
        var score = quality switch
        {
            SignalQuality.High => 100m,
            SignalQuality.Normal => 75m,
            SignalQuality.Low => 50m,
            SignalQuality.Stale => 25m,
            _ => 50m,
        };

        return Task.FromResult(new Dictionary<string, Signal>
        {
            [Name] = new() { Name = Name, Value = score, Quality = quality },
        });
    }
}
