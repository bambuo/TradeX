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

public enum BacktestPhase
{
    Queued,
    FetchingData,
    Running
}

public class BacktestTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DeploymentId { get; init; }
    public Guid StrategyId { get; init; }
    public Guid ExchangeId { get; init; }
    public string StrategyName { get; set; } = string.Empty;
    public string SymbolId { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public decimal InitialCapital { get; set; } = 1000m;
    public BacktestTaskStatus Status { get; set; } = BacktestTaskStatus.Pending;
    public BacktestPhase? Phase { get; set; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
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
    public string? AnalysisJson { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
