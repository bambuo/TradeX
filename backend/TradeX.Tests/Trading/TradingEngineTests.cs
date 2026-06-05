using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Indicators;
using TradeX.Trading;
using TradeX.Trading.Engine;
using TradeX.Trading.Observability;
using TradeX.Trading.Streaming;

namespace TradeX.Tests.Trading;

public class TradingEngineTests
{
    [Fact]
    public void Constructor_CreatesEngine()
    {
        var engine = new TradingEngine();
        Assert.NotNull(engine);
        Assert.IsType<TradingEngine>(engine);
    }

    [Fact]
    public void Status_ReturnsEventDriven()
    {
        Assert.Equal("event-driven", TradingEngine.Status);
    }

    [Fact]
    public void MarketDataCache_LastTradeTime_StoresAndRetrieves()
    {
        var cache = new MarketDataCache();
        cache.LastTradeTime["test-key"] = DateTime.UtcNow;

        Assert.True(cache.LastTradeTime.ContainsKey("test-key"));
    }
}
