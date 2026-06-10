using System.Text.Json;

namespace TradeX.Core.Models;

public class ConditionNode
{
    public string Operator { get; set; } = "AND";
    public List<ConditionNode> Conditions { get; set; } = [];
    public string? Indicator { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    public string? Comparison { get; set; }
    public decimal? Value { get; set; }
    // 相对比较: compareValue = currentValues[Ref] * Value（如 Close > SMA_20 * 1.02）
    public string? Ref { get; set; }
    /// <summary>
    /// 回看 N 根指标快照。>0 时取 N 次评估前的指标值替代当前值做比较。
    /// 用于趋势确认（如"3 根前 RSI < 30"），仅在简单比较（&gt;/&lt;/&gt;=/&lt;=/==）中生效，穿越 CA/CB 忽略此字段。
    /// </summary>
    public int? Lookback { get; set; }
}

public class ExecutionRule
{
    public decimal MaxPositionSize { get; set; } = 100;
    public decimal MaxDailyLoss { get; set; } = 500;
    public decimal SlippageTolerance { get; set; } = 0.001m;
}
