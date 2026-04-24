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
}

public class ExecutionRule
{
    public decimal MaxPositionSize { get; set; } = 100;
    public decimal MaxDailyLoss { get; set; } = 500;
    public decimal SlippageTolerance { get; set; } = 0.001m;
}
