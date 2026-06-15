using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

public class TradingEngineTests
{
    [Fact]
    public void MarketDataCache_LastTradeTime_StoresAndRetrieves()
    {
        var cache = new MarketDataCache();
        cache.LastTradeTime["test-key"] = DateTime.UtcNow;

        Assert.True(cache.LastTradeTime.ContainsKey("test-key"));
    }
}
