using TradeX.Core.Abstractions;
using TradeX.Core.Enums;
using TradeX.Core.Events;

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

public class BacktestTask : AggregateRoot
{
    // EF Core 无参构造函数
    public BacktestTask() { }

    /// <summary>工厂方法：创建回测任务。</summary>
    public static BacktestTask Create(
        Guid strategyId, Guid exchangeId, string strategyName,
        string pair, string timeframe, decimal initialCapital,
        DateTime startAt, DateTime endAt, Guid createdBy,
        decimal? positionSize = null)
    {
        return new BacktestTask
        {
            StrategyId = strategyId,
            ExchangeId = exchangeId,
            StrategyName = strategyName,
            Pair = pair,
            Timeframe = timeframe,
            InitialCapital = initialCapital,
            PositionSize = positionSize,
            StartAt = startAt,
            EndAt = endAt,
            CreatedBy = createdBy
        };
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StrategyId { get; init; }
    public Guid ExchangeId { get; init; }
    public string StrategyName { get; set; } = string.Empty;
    public string Pair { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public decimal InitialCapital { get; set; } = 1000m;
    public decimal? PositionSize { get; set; }
    public BacktestTaskStatus Status { get; set; } = BacktestTaskStatus.Pending;
    public BacktestPhase? Phase { get; set; }
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // ─────────────── 领域方法 ───────────────

    public void Start()
    {
        if (Status != BacktestTaskStatus.Pending)
            throw new InvalidOperationException($"回测任务 {Id} 状态为 {Status}，不能启动");
        Status = BacktestTaskStatus.Running;
        Phase = BacktestPhase.FetchingData;
        AddDomainEvent(new BacktestStartedEvent(Id));
    }

    public void Complete(decimal? finalValue = null, decimal? totalReturnPercent = null)
    {
        if (Status != BacktestTaskStatus.Running)
            throw new InvalidOperationException($"回测任务 {Id} 状态为 {Status}，不能标记完成");
        Status = BacktestTaskStatus.Completed;
        Phase = null;
        CompletedAt = DateTime.UtcNow;
        AddDomainEvent(new BacktestCompletedEvent(Id, finalValue ?? 0, totalReturnPercent ?? 0));
    }

    public void Fail(string? reason = null)
    {
        Status = BacktestTaskStatus.Failed;
        Phase = null;
        CompletedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == BacktestTaskStatus.Completed || Status == BacktestTaskStatus.Failed)
            return;
        Status = BacktestTaskStatus.Cancelled;
        Phase = null;
        CompletedAt = DateTime.UtcNow;
    }
}
