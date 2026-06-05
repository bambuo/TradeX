using TradeX.Core.Abstractions;
using TradeX.Core.Enums;
using TradeX.Core.Events;
using TradeX.Core.Interfaces;

namespace TradeX.Core.Models;

/// <summary>
/// 持仓聚合根。生产代码使用 <see cref="UpdateMarketPrice"/> / <see cref="Close"/> 而非直接 setter。
///
/// 状态机：
/// <code>
/// Open ──UpdateMarketPrice──► Open (持续刷新 CurrentPrice/UnrealizedPnl)
///      ──Close──► Closed (终态)
/// </code>
/// </summary>
public class Position : AggregateRoot, IVersioned
{
    // EF Core 无参构造函数（公开以兼容现有代码）
    public Position() { }

    /// <summary>工厂方法：开仓。</summary>
    public static Position Open(
        Guid traderId, Guid exchangeId, Guid strategyId,
        string pair, decimal quantity, decimal entryPrice)
    {
        var pos = new Position
        {
            TraderId = traderId,
            ExchangeId = exchangeId,
            StrategyId = strategyId,
            Pair = pair,
            Quantity = quantity,
            EntryPrice = entryPrice,
            CurrentPrice = entryPrice
        };
        pos.AddDomainEvent(new PositionOpenedDomainEvent(
            pos.Id, traderId, strategyId, pair, quantity, entryPrice));
        return pos;
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TraderId { get; init; }
    public Guid ExchangeId { get; init; }
    public Guid StrategyId { get; init; }
    /// <summary>
    /// 开仓买单的订单 Id。作为"成交→持仓"投影的幂等键：同一笔成交无论被实盘同步路径
    /// 还是对账器恢复路径重复处理，凭此唯一约束保证只开一条持仓。
    /// </summary>
    public Guid? OpeningOrderId { get; set; }
    public string Pair { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public PositionStatus Status { get; set; } = PositionStatus.Open;
    public DateTime OpenedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>乐观并发控制版本号，由 VersionInterceptor 在保存前自动刷新。</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    // ─────────────── 领域方法 ───────────────

    /// <summary>
    /// 用当前市价刷新持仓（仅对 Open 状态生效）。
    /// 计算未实现盈亏 = (CurrentPrice - EntryPrice) * Quantity。
    /// </summary>
    public void UpdateMarketPrice(decimal currentPrice)
    {
        if (currentPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(currentPrice), "价格不能为负");
        if (Status != PositionStatus.Open)
            throw new InvalidOperationException($"持仓 {Id} 已 {Status}，不能更新价格");

        CurrentPrice = currentPrice;
        UnrealizedPnl = (currentPrice - EntryPrice) * Quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 关闭持仓，记录实现盈亏并清空未实现盈亏。Open → Closed 转换。
    /// </summary>
    public void Close(decimal exitPrice)
    {
        if (Status != PositionStatus.Open)
            throw new InvalidOperationException($"持仓 {Id} 已 {Status}，不能再次关闭");

        RealizedPnl = (exitPrice - EntryPrice) * Quantity;
        UnrealizedPnl = 0;
        CurrentPrice = exitPrice;
        Status = PositionStatus.Closed;
        var now = DateTime.UtcNow;
        ClosedAtUtc = now;
        UpdatedAt = now;

        AddDomainEvent(new PositionClosedDomainEvent(
            Id, TraderId, Pair, Quantity, EntryPrice, exitPrice, RealizedPnl));
    }
}
