namespace TradeX.Trading;

public class RiskSettings
{
    public decimal MaxDailyLoss { get; set; } = 1000;
    public decimal MaxDrawdownPercent { get; set; } = 20;
    public int MaxConsecutiveLosses { get; set; } = 3;
    public int MaxOpenPositions { get; set; } = 10;
    public decimal SlippageTolerance { get; set; } = 0.001m;
    public decimal MaxSlippageAmount { get; set; } = 10;
    public bool CircuitBreakerActive { get; set; }
    public int CooldownSeconds { get; set; } = 300;
}
