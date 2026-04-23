namespace BscSmartMoneyBot.Core.Models;

public class TransactionDetail
{
    public decimal ServiceChargeUsd { get; set; }
    public string TxHash { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
