using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TradeX.Trading.Engine;

public record VolatilityGridExecutionRule(
    string Type,
    decimal EntryVolatilityPercent,
    decimal RebalancePercent,
    decimal BasePositionSize,
    decimal MaxPositionSize,
    int MaxPyramidingLevels,
    bool NoStopLoss,
    decimal SlippageTolerance,
    decimal MaxDailyLoss)
{
    public static VolatilityGridExecutionRule Default { get; } = new(
        Type: "volatility_grid",
        EntryVolatilityPercent: 1m,
        RebalancePercent: 1m,
        BasePositionSize: 100m,
        MaxPositionSize: 500m,
        MaxPyramidingLevels: 5,
        NoStopLoss: true,
        SlippageTolerance: 0.0005m,
        MaxDailyLoss: 200m);
}

public static class VolatilityGridExecutionRuleParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static VolatilityGridExecutionRule? TryParse(string executionRuleJson, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(executionRuleJson) || executionRuleJson == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(executionRuleJson);
            if (!doc.RootElement.TryGetProperty("type", out var typeNode)
                || !string.Equals(typeNode.GetString(), "volatility_grid", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parsed = JsonSerializer.Deserialize<VolatilityGridExecutionRuleDto>(executionRuleJson, JsonOptions);
            if (parsed is null)
                return VolatilityGridExecutionRule.Default;

            return new VolatilityGridExecutionRule(
                Type: "volatility_grid",
                EntryVolatilityPercent: parsed.EntryVolatilityPercent > 0 ? parsed.EntryVolatilityPercent : 1m,
                RebalancePercent: parsed.RebalancePercent > 0 ? parsed.RebalancePercent : 1m,
                BasePositionSize: parsed.BasePositionSize > 0 ? parsed.BasePositionSize : 100m,
                MaxPositionSize: parsed.MaxPositionSize > 0 ? parsed.MaxPositionSize : 500m,
                MaxPyramidingLevels: parsed.MaxPyramidingLevels >= 0 ? parsed.MaxPyramidingLevels : 5,
                NoStopLoss: parsed.NoStopLoss,
                SlippageTolerance: parsed.SlippageTolerance >= 0 ? parsed.SlippageTolerance : 0.0005m,
                MaxDailyLoss: parsed.MaxDailyLoss >= 0 ? parsed.MaxDailyLoss : 200m);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "波动率网格执行规则解析失败，回退到默认值");
            return VolatilityGridExecutionRule.Default;
        }
    }

    private sealed class VolatilityGridExecutionRuleDto
    {
        public decimal EntryVolatilityPercent { get; init; }
        public decimal RebalancePercent { get; init; }
        public decimal BasePositionSize { get; init; }
        public decimal MaxPositionSize { get; init; }
        public int MaxPyramidingLevels { get; init; }
        public bool NoStopLoss { get; init; }
        public decimal SlippageTolerance { get; init; }
        public decimal MaxDailyLoss { get; init; }
    }
}
