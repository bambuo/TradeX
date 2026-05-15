using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Core.Models;

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
}
