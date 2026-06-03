namespace TradeX.Core.Models;

public class BacktestResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TaskId { get; init; }
    public string StrategyName { get; set; } = string.Empty;
    public string Pair { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public decimal InitialCapital { get; set; }
    public decimal FinalValue { get; set; }
    public int TotalTrades { get; set; }
    public decimal TotalReturnPercent { get; set; }
    public decimal AnnualizedReturnPercent { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal WinRate { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal ProfitLossRatio { get; set; }
    public string Details { get; set; } = "[]";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
