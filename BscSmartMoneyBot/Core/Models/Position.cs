using System.Text.Json.Serialization;
using BscSmartMoneyBot.Core.Enums;

namespace BscSmartMoneyBot.Core.Models;

public class Position
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TokenAddress { get; set; } = string.Empty;
    public string TokenSymbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPriceUSD { get; set; }
    public decimal BuyCostUSD { get; set; }
    public decimal BuyFeeUSD { get; set; }
    public decimal EstSellFeeUSD { get; set; }
    public decimal MaxPriceUSD { get; set; }
    public bool TrailingActive { get; set; }
    public string BuyTxHash { get; set; } = string.Empty;
    public DateTime BuyTime { get; set; }
    public PositionStatus Status { get; set; } = PositionStatus.Open;

    public List<int> TpTaken { get; set; } = [];

    [JsonIgnore]
    public decimal CurrentPriceUSD { get; set; }

    [JsonIgnore]
    public decimal CurrentValueUSD => Quantity * CurrentPriceUSD;

    [JsonIgnore]
    public decimal NetPnLUSD => CurrentValueUSD - BuyCostUSD - BuyFeeUSD - EstSellFeeUSD;

    [JsonIgnore]
    public decimal NetPnLPercent => BuyCostUSD > 0 ? (NetPnLUSD / BuyCostUSD) * 100 : 0;

    public void UpdatePrice(decimal currentPrice)
    {
        CurrentPriceUSD = currentPrice;

        if (currentPrice > MaxPriceUSD)
        {
            MaxPriceUSD = currentPrice;
        }
    }
}
