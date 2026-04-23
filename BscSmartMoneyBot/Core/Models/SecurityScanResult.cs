namespace BscSmartMoneyBot.Core.Models;

public class SecurityScanResult
{
    public string RiskLevel { get; set; } = "UNKNOWN";
    public List<string> RiskLabels { get; set; } = [];
    public bool IsChainSupported { get; set; } = true;
}
