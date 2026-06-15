namespace TradeX.Core.Enums;

/// <summary>信号质量等级。数值越小越好（0=High, 3=Stale）。</summary>
public enum SignalQuality
{
    High = 0,
    Normal = 1,
    Low = 2,
    Stale = 3,
}
