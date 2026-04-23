namespace BscSmartMoneyBot.Core.Models;

public class SwapResult
{
    public string SwapTxHash { get; set; } = string.Empty;
    public decimal FromAmount { get; set; }
    public decimal ToAmount { get; set; }
    public decimal PriceImpact { get; set; }
    public decimal GasUsed { get; set; }
}
