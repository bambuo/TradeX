namespace BscSmartMoneyBot.Core.Enums;

public enum PositionStatus
{
    Open,
    StopLossTriggered,
    TakeProfitTriggered,
    TrailingStopTriggered,
    Closed
}

public enum RiskLevel
{
    UNKNOWN,
    LOW,
    MEDIUM,
    HIGH,
    CRITICAL
}
