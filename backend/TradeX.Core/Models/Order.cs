using TradeX.Core.Enums;

namespace TradeX.Core.Models;

public class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TraderId { get; init; }
    public string? ExchangeOrderId { get; set; }
    public Guid ExchangeId { get; init; }
    public Guid? StrategyId { get; set; }
    public Guid? PositionId { get; set; }
    public string SymbolId { get; set; } = string.Empty;
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
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
