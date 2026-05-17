using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class TradingEngineTests
{
    private readonly TradingEngine _engine;
    private readonly MarketDataCache _cache;
    private readonly ITradingEventBus _eventBus;

    public TradingEngineTests()
    {
        _cache = new MarketDataCache();
        _eventBus = Substitute.For<ITradingEventBus>();

        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IPositionRepository>());
        services.AddScoped(_ => Substitute.For<IStrategyRepository>());
        services.AddScoped(_ => Substitute.For<IOrderRepository>());
        services.AddScoped(_ => Substitute.For<ITradeExecutor>());
        services.AddScoped(_ => Substitute.For<IPortfolioRiskManager>());
        services.AddScoped(_ => Substitute.For<IExchangeClientFactory>());
        services.AddScoped(_ => Substitute.For<IExchangeRepository>());
        services.AddScoped(_ => Substitute.For<IEncryptionService>());

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        _engine = new TradingEngine(scopeFactory, _cache, _eventBus,
            new TradeX.Trading.Observability.TradeXMetrics(),
            Microsoft.Extensions.Options.Options.Create(new RiskSettings()),
            Substitute.For<ILogger<TradingEngine>>());
    }

    [Fact]
    public void Constructor_CreatesEngine()
    {
        Assert.NotNull(_engine);
        Assert.IsType<TradingEngine>(_engine);
    }

    [Fact]
    public async Task StartAsync_CompletesWithoutException()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);
        await _engine.StartAsync(cts.Token);
    }

    [Fact]
    public async Task StartAsync_WithCacheAndBus_DoesNotThrow()
    {
        _cache.PriceHistory.TryAdd("BTCUSDT", [50000, 50100, 50200]);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(200);
        await _engine.StartAsync(cts.Token);
    }
}
