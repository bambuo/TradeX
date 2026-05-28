using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Streaming;

namespace TradeX.Tests.Trading;

public class KlineStreamManagerTests
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

        var btcState = manager.GetState(BuildKey(ExchangeId, "BTCUSDT", "15m"));
        Assert.NotNull(btcState);
        Assert.Equal("BTCUSDT", btcState!.Pair);
        Assert.Equal("15m", btcState.Interval);

        var ethState = manager.GetState(BuildKey(ExchangeId, "ETHUSDT", "15m"));
        Assert.NotNull(ethState);
        Assert.Equal("ETHUSDT", ethState!.Pair);
    }

    [Fact]
    public async Task RefreshSubscriptionsAsync_DefaultTimeframe()
    {
        var binding = new StrategyBinding
        {
            Id = Guid.NewGuid(),
            TraderId = Guid.NewGuid(),
            ExchangeId = ExchangeId,
            Pairs = "BTCUSDT",
            Timeframe = "",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        await manager.RefreshSubscriptionsAsync(default);

        var state = manager.GetState(BuildKey(ExchangeId, "BTCUSDT", "15m"));
        Assert.NotNull(state);
        Assert.Equal("15m", state!.Interval);
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
            Timeframe = "15m",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        await manager.RefreshSubscriptionsAsync(default);
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "BTCUSDT", "15m")));

        binding.Pairs = "ETHUSDT";
        await manager.RefreshSubscriptionsAsync(default);
        Assert.Null(manager.GetState(BuildKey(ExchangeId, "BTCUSDT", "15m")));
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "ETHUSDT", "15m")));
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
            Timeframe = "15m",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        await manager.RefreshSubscriptionsAsync(default);
        await manager.RefreshSubscriptionsAsync(default);

        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "BTCUSDT", "15m")));
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "ETHUSDT", "15m")));
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
            Timeframe = "15m",
            Status = BindingStatus.Active
        };

        var (manager, _) = Build(activeBindings: [binding]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await manager.StartAsync(cts.Token);
        Assert.NotNull(manager.GetState(BuildKey(ExchangeId, "BTCUSDT", "15m")));

        await manager.StopAsync();
        Assert.Null(manager.GetState(BuildKey(ExchangeId, "BTCUSDT", "15m")));
    }

    [Fact]
    public async Task GetState_UnknownKey_ReturnsNull()
    {
        var (manager, _) = Build();
        Assert.Null(manager.GetState("nonexistent"));
    }

    // ─────────────── Test Infrastructure ───────────────

    private static (KlineStreamManager, IServiceScopeFactory) Build(
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

        var services = new ServiceCollection();
        services.AddSingleton(bindingRepo);
        services.AddSingleton(exchangeRepo);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var klineChannel = Channel.CreateBounded<KlineEvent>(new BoundedChannelOptions(100)
        { FullMode = BoundedChannelFullMode.DropOldest });
        var logger = Substitute.For<ILogger<KlineStreamManager>>();

        var manager = new KlineStreamManager(scopeFactory, klineChannel, logger);
        return (manager, scopeFactory);
    }

    private static string BuildKey(Guid exchangeId, string pair, string interval = "15m")
        => $"{exchangeId:N}:{pair.ToUpperInvariant()}:{interval.ToLowerInvariant()}";
}
