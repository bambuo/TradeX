using TradeX.Core.Enums;

namespace TradeX.Core.Models;

public enum BacktestTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public class BacktestTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StrategyId { get; init; }
    public BacktestTaskStatus Status { get; set; } = BacktestTaskStatus.Pending;
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

public class BacktestResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TaskId { get; init; }
    public decimal TotalReturnPercent { get; set; }
    public decimal AnnualizedReturnPercent { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal ProfitLossRatio { get; set; }
    public string DetailJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
