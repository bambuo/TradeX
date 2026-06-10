namespace TradeX.Rules.Indicators;

/// <summary>上下文指标常量</summary>
public static class ContextIndicators
{
    /// <summary>偏离度：(当前价 - 均价) / 均价 * 100</summary>
    public const string DeviationFromAvg = "DEVIATION_FROM_AVG";

    /// <summary>加仓层数</summary>
    public const string PyramidingLevel = "PYRAMIDING_LEVEL";

    /// <summary>持仓名义价值</summary>
    public const string PositionNotional = "POSITION_NOTIONAL";

    /// <summary>持仓盈亏百分比</summary>
    public const string PositionPnlPct = "POSITION_PNL_PCT";

    /// <summary>持仓数量</summary>
    public const string PositionCount = "POSITION_COUNT";

    /// <summary>
    /// 全部上下文指标名。供校验器识别——这些指标由持仓状态派生，不在技术指标注册表中，
    /// 但规则的 when/sizeMultiplierRef 引用它们是合法的。
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        DeviationFromAvg, PyramidingLevel, PositionNotional, PositionPnlPct, PositionCount
    };
}

/// <summary>上下文指标计算器</summary>
public static class ContextIndicatorCalculator
{
    public static Dictionary<string, decimal> Calculate(
        decimal currentPrice,
        decimal averageEntryPrice,
        decimal quantityHeld,
        int lotCount)
    {
        var result = new Dictionary<string, decimal>
        {
            [ContextIndicators.PyramidingLevel] = lotCount,
            [ContextIndicators.PositionCount] = lotCount,
            [ContextIndicators.PositionNotional] = quantityHeld * currentPrice
        };

        if (averageEntryPrice > 0)
        {
            result[ContextIndicators.DeviationFromAvg] =
                (currentPrice - averageEntryPrice) / averageEntryPrice * 100m;
        }

        if (quantityHeld > 0 && averageEntryPrice > 0)
        {
            result[ContextIndicators.PositionPnlPct] =
                (currentPrice - averageEntryPrice) / averageEntryPrice * 100m;
        }

        return result;
    }
}
