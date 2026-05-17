namespace TradeX.Trading.Risk;

public class RiskContext
{
    public Guid TraderId { get; init; }
    public Guid ExchangeId { get; init; }
    public string? Pair { get; set; }
    public decimal? OrderQuantity { get; set; }
    public decimal? OrderPrice { get; set; }
    public decimal DailyLoss { get; set; }
    public decimal DailyProfit { get; set; }
    public decimal PortfolioValue { get; set; }
    public int ConsecutiveLossCount { get; set; }
    public decimal OpenPositionCount { get; set; }
    public decimal MaxDailyLoss { get; set; } = 1000;
    public decimal MaxDrawdownPercent { get; set; } = 20;
    public int MaxConsecutiveLosses { get; set; } = 3;
    public int MaxOpenPositions { get; set; } = 10;
    public decimal SlippageTolerance { get; set; } = 0.001m;
    public decimal MaxSlippageAmount { get; set; } = 10;
    public bool CircuitBreakerActive { get; set; }
    public DateTime? LastTradeTimeUtc { get; set; }
    public int CooldownSeconds { get; set; } = 300;
    public decimal? OrderNotional { get; set; }
    public decimal MaxOrderNotional { get; set; } = 0;
    public List<string> DeniedReasons { get; } = [];
    public bool IsAllowed => DeniedReasons.Count == 0;

    public void Deny(string reason)
    {
        DeniedReasons.Add(reason);
    }
}

public interface IRiskCheckHandler
{
    IRiskCheckHandler SetNext(IRiskCheckHandler handler);
    Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default);
}

public abstract class RiskCheckHandler : IRiskCheckHandler
{
    private IRiskCheckHandler? _next;

    public IRiskCheckHandler SetNext(IRiskCheckHandler handler)
    {
        _next = handler;
        return handler;
    }

    public virtual async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (_next is not null)
            return await _next.CheckAsync(context, ct);
        return context;
    }
}
