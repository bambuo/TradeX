namespace TradeX.Core.Models;

public class Symbol
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ExchangeId { get; init; }
    public string SymbolName { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public class ExchangeSymbolRuleSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ExchangeId { get; init; }
    public string Symbol { get; set; } = string.Empty;
    public int PricePrecision { get; set; }
    public int QuantityPrecision { get; set; }
    public decimal MinNotional { get; set; }
    public decimal MinQuantity { get; set; }
    public decimal TickSize { get; set; }
    public decimal StepSize { get; set; }
    public DateTime FetchedAtUtc { get; init; } = DateTime.UtcNow;
}
