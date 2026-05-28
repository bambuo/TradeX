using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Streaming;

namespace TradeX.Tests.Trading;

public class TradeStreamManagerTests
{
    private static readonly Guid ExchangeId = Guid.NewGuid();

    [Fact]
    public async Task RefreshSubscriptionsAsync_NoBindings_NoSubscriptions()
    {
        var (manager, _) = Build(activeBindings: []);
        await manager.RefreshSubscriptionsAsync(default);

        Assert.Null(manager.GetState(BuildKey(ExchangeId, "BTCUSDT")));
    }

    [Fact]
    public async Task RefreshSubscriptionsAsync_ActiveBindings_CreatesSubscriptions()
    {
        var binding = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            TraderId = Guid.NewGuid(),
            ExchangeId = ExchangeId,
            Pairs = "BTCUSDT,ETHUSDT",
            Timeframe = "15m",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        await manager.RefreshSubscriptionsAsync(default);

        var btcState = manager.GetState(BuildKey(ExchangeId, "BTCUSDT"));
        Assert.NotNull(btcState);
        Assert.Equal("BTCUSDT", btcState!.Pair);
        Assert.Equal(ExchangeId, btcState.ExchangeId);

        var ethState = manager.GetState(BuildKey(ExchangeId, "ETHUSDT"));
        Assert.NotNull(ethState);
        Assert.Equal("ETHUSDT", ethState!.Pair);
    }

    [Fact]
    public async Task RefreshSubscriptionsAsync_RemovesStaleSubscriptions()
    {
        var binding = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            TraderId = Guid.NewGuid(),
            ExchangeId = ExchangeId,
            Pairs = "BTCUSDT",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        await manager.RefreshSubscriptionsAsync(default);
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "BTCUSDT")));

        // Second refresh with different pair
        binding.Pairs = "ETHUSDT";
        await manager.RefreshSubscriptionsAsync(default);
        Assert.Null(manager.GetState(BuildKey(ExchangeId, "BTCUSDT")));
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "ETHUSDT")));
    }

    [Fact]
    public async Task RefreshSubscriptionsAsync_PreservesExistingSubscriptions()
    {
        var binding = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            TraderId = Guid.NewGuid(),
            ExchangeId = ExchangeId,
            Pairs = "BTCUSDT,ETHUSDT",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        await manager.RefreshSubscriptionsAsync(default);

        // Second refresh — same pairs, should preserve
        await manager.RefreshSubscriptionsAsync(default);

        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "BTCUSDT")));
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "ETHUSDT")));
    }

    [Fact]
    public async Task RefreshSubscriptionsAsync_MultipleBindingsSamePair_Deduplicates()
    {
        var exchangeId1 = Guid.NewGuid();
        var exchangeId2 = Guid.NewGuid();

        var binding1 = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            TraderId = Guid.NewGuid(),
            ExchangeId = exchangeId1,
            Pairs = "BTCUSDT",
            Status = BindingStatus.Active
        };
        var binding2 = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            TraderId = Guid.NewGuid(),
            ExchangeId = exchangeId2,
            Pairs = "BTCUSDT",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding1, binding2], exchanges:
        [
            new TradeX.Core.Models.Exchange { Id = exchangeId1, Type = ExchangeType.Binance },
            new TradeX.Core.Models.Exchange { Id = exchangeId2, Type = ExchangeType.Binance }
        ]);

        await manager.RefreshSubscriptionsAsync(default);

        // Different exchange IDs → two separate subscriptions (same pair different exchange)
        var state1 = manager.GetState(BuildKey(exchangeId1, "BTCUSDT"));
        var state2 = manager.GetState(BuildKey(exchangeId2, "BTCUSDT"));
        Assert.NotNull(state1);
        Assert.NotNull(state2);
    }

    [Fact]
    public async Task StartStopAsync_Lifecycle_Succeeds()
    {
        var binding = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            TraderId = Guid.NewGuid(),
            ExchangeId = ExchangeId,
            Pairs = "BTCUSDT",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await manager.StartAsync(cts.Token);
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "BTCUSDT")));

        await manager.StopAsync();
        Assert.Null(manager.GetState(BuildKey(ExchangeId, "BTCUSDT")));
    }

    // ─────────────── Test Infrastructure ───────────────

    private static (TradeStreamManager, IServiceScopeFactory) Build(
        List<StrategyBinding>? activeBindings = null,
        List<TradeX.Core.Models.Exchange>? exchanges = null)
    {
        activeBindings ??= [];

        var bindingRepo = Substitute.For<IStrategyBindingRepository>();
        bindingRepo.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(activeBindings);

        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var id = call.Arg<Guid>();
                return exchanges?.FirstOrDefault(e => e.Id == id)
                    ?? new TradeX.Core.Models.Exchange { Id = id, Type = ExchangeType.Binance, ApiKeyEncrypted = "enc", SecretKeyEncrypted = "enc" };
            });

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        var client = Substitute.For<IExchangeClient>();
        client.SubscribeTradesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<Trade>());
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(client);

        // Build scope factory
        var services = new ServiceCollection();
        services.AddSingleton(bindingRepo);
        services.AddSingleton(exchangeRepo);
        services.AddSingleton(clientFactory);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var tradeChannel = Channel.CreateBounded<TradeEvent>(new BoundedChannelOptions(100)
        { FullMode = BoundedChannelFullMode.DropOldest });
        var logger = Substitute.For<ILogger<TradeStreamManager>>();

        var manager = new TradeStreamManager(scopeFactory, tradeChannel, logger);
        return (manager, scopeFactory);
    }

    private static string BuildKey(Guid exchangeId, string pair)
        => $"{exchangeId:N}:{pair.ToUpperInvariant()}";
}
