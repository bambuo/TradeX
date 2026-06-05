namespace TradeX.Core.Models;

/// <summary>
/// 定时从交易所拉取的历史订单记录。用于支撑本地数据库级分页查询。
/// 通过 <c>[ExchangeId, ExchangeOrderId]</c> 唯一约束做 upsert 去重。
/// </summary>
public class ExchangeOrderHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ExchangeId { get; init; }
    public string Pair { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public string ExchangeOrderId { get; set; } = string.Empty;
    public DateTime PlacedAt { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}
