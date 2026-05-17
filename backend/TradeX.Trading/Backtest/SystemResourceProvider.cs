using System.Diagnostics;

namespace TradeX.Trading.Backtest;

public class SystemResourceProvider : IResourceProvider
{
    private static readonly Process Process = Process.GetCurrentProcess();

    public long GetCurrentMemoryBytes() => GC.GetTotalMemory(false);

    public TimeSpan GetTotalProcessorTime() => Process.TotalProcessorTime;

    public int GetProcessorCount() => Environment.ProcessorCount;
}
