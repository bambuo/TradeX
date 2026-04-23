using System.Text.Json.Serialization;

namespace BscSmartMoneyBot.Core.Models;

public class Signal
{
    public string TokenAddress { get; set; } = string.Empty;
    public string TokenSymbol { get; set; } = string.Empty;
    public string TokenName { get; set; } = string.Empty;
    public decimal MarketCap { get; set; }
    public decimal Liquidity { get; set; }
    public decimal SoldRatio { get; set; }
    public int SmartMoneyWallets { get; set; }
    public DateTime SignalTime { get; set; }
    public decimal PriceUSD { get; set; }
    public string RiskLevel { get; set; } = "UNKNOWN";
    public List<string> RiskLabels { get; set; } = [];

    [JsonIgnore]
    public decimal Score { get; set; }
}
