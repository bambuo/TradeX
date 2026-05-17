namespace TradeX.Trading.Backtest;

public interface IResourceProvider
{
    long GetCurrentMemoryBytes();
    TimeSpan GetTotalProcessorTime();
    int GetProcessorCount();
}
