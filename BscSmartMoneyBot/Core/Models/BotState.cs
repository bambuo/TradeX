namespace BscSmartMoneyBot.Core.Models;

public class BotState
{
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    public Dictionary<string, DateTime> SeenSignals { get; set; } = [];
    public Dictionary<string, Position> OpenPositions { get; set; } = [];

    public DateTime? LastBuyTime { get; set; }
    public int TotalBuys { get; set; }
    public int TotalSells { get; set; }
    public decimal TotalProfitUSD { get; set; }
    public decimal TotalLossUSD { get; set; }
}
