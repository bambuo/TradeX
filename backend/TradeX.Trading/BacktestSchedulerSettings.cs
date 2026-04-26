namespace TradeX.Trading;

public class BacktestSchedulerSettings
{
    public int MaxConcurrency { get; init; } = 3;
    public int TaskTimeoutMinutes { get; init; } = 30;
    public int MonitorIntervalSeconds { get; init; } = 5;
    public long MemoryWarningMb { get; init; } = 512;
    public long MemoryCriticalMb { get; init; } = 1024;
    public long MemoryAbsoluteMb { get; init; } = 1536;
    public int CpuWarningPercent { get; init; } = 50;
    public int CpuCriticalPercent { get; init; } = 75;
    public int CpuAbsolutePercent { get; init; } = 90;
}
