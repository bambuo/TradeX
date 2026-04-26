namespace TradeX.Trading;

public interface IResourceProvider
{
    long GetCurrentMemoryBytes();
    TimeSpan GetTotalProcessorTime();
    int GetProcessorCount();
}
