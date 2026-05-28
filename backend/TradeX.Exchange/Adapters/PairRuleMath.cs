namespace TradeX.Exchange.Adapters;

/// <summary>交易对精度 ↔ 步进的换算助手。</summary>
internal static class PairRuleMath
{
    /// <summary>精度位数 → 步进（precision=3 → 0.001；precision&lt;=0 → 1）。</summary>
    public static decimal StepFromPrecision(int precision)
    {
        if (precision <= 0) return 1m;
        decimal step = 1m;
        for (var i = 0; i < precision; i++) step /= 10m;
        return step;
    }

    /// <summary>decimal 的小数位数（0.001 → 3）。用于由步进/tick 反推展示精度。</summary>
    public static int PrecisionFromStep(decimal step)
        => step <= 0 ? 0 : (decimal.GetBits(step)[3] >> 16) & 0xFF;
}
