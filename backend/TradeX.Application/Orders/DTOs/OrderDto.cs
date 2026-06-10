namespace TradeX.Application.Orders.DTOs;

/// <summary>
/// 订单数据传输对象。适配器模式 — 将领域对象转换为前端友好的扁平结构。
/// </summary>
public sealed record OrderDto(
    Guid Id,
    Guid TraderId,
    string Pair,
    string Side,
    string Type,
    string Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? Price,
    decimal QuoteQuantity,
    decimal Fee,
    string? FeeAsset,
    bool IsManual,
    DateTime PlacedAt,
    DateTime UpdatedAt);
