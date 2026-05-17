using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Core.Models;

/// <summary>
/// 订单聚合根。状态转换通过领域方法完成，setter 保留 public 以兼容 EF Core 物化与既有 controller，
/// 但**生产代码请使用 <see cref="MarkPlaced"/> / <see cref="RecordFill"/> / <see cref="MarkFailed"/> /
/// <see cref="MarkCancelled"/>** —— 这些方法封装了状态机不变量。
///
/// 状态机：
/// <code>
/// Pending ──MarkPlaced──► Pending (附加 ExchangeOrderId)
///        ──RecordFill──► PartiallyFilled / Filled
///        ──MarkFailed──► Failed (终态)
///        ──MarkCancelled──► Cancelled (终态)
/// PartiallyFilled ──RecordFill──► Filled (终态)
///                 ──MarkCancelled──► Cancelled (终态)
/// </code>
/// </summary>
public class Order : IVersioned
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TraderId { get; init; }
    /// <summary>客户端订单 ID（幂等键）。提交至交易所前生成，用于断线/崩溃后对账。</summary>
    public Guid ClientOrderId { get; init; } = Guid.NewGuid();
    public string? ExchangeOrderId { get; set; }
    public Guid ExchangeId { get; init; }
    public Guid? StrategyId { get; set; }
    public Guid? PositionId { get; set; }
    public string Pair { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal? Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal QuoteQuantity { get; set; }
    public decimal Fee { get; set; }
    public string? FeeAsset { get; set; }
    public bool IsManual { get; set; }
    public DateTime PlacedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>乐观并发控制版本号，由 VersionInterceptor 在保存前自动刷新。</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    // ─────────────── 领域方法 ───────────────

    private static readonly OrderStatus[] TerminalStatuses =
        { OrderStatus.Filled, OrderStatus.Failed, OrderStatus.Cancelled };

    /// <summary>记录交易所已受理并返回订单 ID。状态保持 Pending（未必成交）。</summary>
    public void MarkPlaced(string exchangeOrderId)
    {
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
            throw new ArgumentException("ExchangeOrderId 不能为空", nameof(exchangeOrderId));
        EnsureNotTerminal(nameof(MarkPlaced));
        ExchangeOrderId = exchangeOrderId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>记录成交。根据已成交量与申报量决定 Filled / PartiallyFilled。</summary>
    public void RecordFill(decimal filledQuantity, decimal fee, string? exchangeOrderId = null)
    {
        if (filledQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(filledQuantity), "成交数量不能为负");
        EnsureNotTerminal(nameof(RecordFill));
        if (exchangeOrderId is not null)
            ExchangeOrderId = exchangeOrderId;
        FilledQuantity = filledQuantity;
        Fee = fee;
        Status = filledQuantity >= Quantity && Quantity > 0
            ? OrderStatus.Filled
            : filledQuantity > 0
                ? OrderStatus.PartiallyFilled
                : OrderStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>标记下单失败（交易所拒绝、网络错误等）。</summary>
    public void MarkFailed(string? reason = null)
    {
        EnsureNotTerminal(nameof(MarkFailed));
        Status = OrderStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>标记订单已取消。</summary>
    public void MarkCancelled()
    {
        EnsureNotTerminal(nameof(MarkCancelled));
        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>是否已到达终态（不可再转换）。</summary>
    public bool IsTerminal() => Array.IndexOf(TerminalStatuses, Status) >= 0;

    private void EnsureNotTerminal(string operation)
    {
        if (IsTerminal())
            throw new InvalidOperationException(
                $"订单 {Id} 已是终态 {Status}，不能执行 {operation}");
    }
}
