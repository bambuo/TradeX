using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Rules.Engine;
using TradeX.Trading.Engine;
using TradeX.Trading.Execution;
using TradeX.Trading.EventBus;
using TradeX.Trading.Observability;
using TradeX.Trading.Risk;
using TradeX.Trading.Streaming;

namespace TradeX.Tests.Trading;

public class StrategyEvaluationConsumerTests
{
    private static readonly Guid ExchangeId = Guid.NewGuid();
    private static readonly Guid TraderId = Guid.NewGuid();
    private static readonly Guid StrategyId = Guid.NewGuid();
    private static readonly Guid BindingId = Guid.NewGuid();

    /// <summary>
    /// Trade 事件触发完整的策略评估管道：条件满足 → 风控通过 → 下单成功 → 事件发布。
    /// </summary>
    [Fact]
    public async Task TradeEvent_EntryConditionMet_PlacesOrderAndPublishesEvent()
    {
        var indRegistry = new IndicatorRegistry();
        indRegistry.Register("RSI", _ => 40m); // Always 40 → RSI > 30 = true

        var (consumer, services, tradeChannel, klineChannel, eventBus, tradeExecutor, riskManager) =
            BuildConsumer(indRegistry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Start consumer (this starts ExecuteAsync)
        await consumer.StartAsync(cts.Token);
        await Task.Delay(500); // Let strategy cache load

        // Send two trade events: Trade evaluation needs a previous price window.
        var firstTradeEvent = new TradeEvent(
            "BTCUSDT", ExchangeType.Binance, ExchangeId,
            new Trade(DateTime.UtcNow.AddSeconds(-1), 49900m, 0.1m, IsBuyerMaker: false));
        var secondTradeEvent = new TradeEvent(
            "BTCUSDT", ExchangeType.Binance, ExchangeId,
            new Trade(DateTime.UtcNow, 50000m, 0.1m, IsBuyerMaker: false));
        await tradeChannel.Writer.WriteAsync(firstTradeEvent, cts.Token);
        await tradeChannel.Writer.WriteAsync(secondTradeEvent, cts.Token);

        // Wait for processing
        await Task.Delay(1000);

        // Verify: Order was placed
        await tradeExecutor.Received(1).ExecuteMarketOrderAsync(
            Arg.Is<Order>(o =>
                o.TraderId == TraderId &&
                o.ExchangeId == ExchangeId &&
                o.Pair == "BTCUSDT" &&
                o.Side == OrderSide.Buy &&
                o.Quantity == 0m &&
                o.QuoteQuantity > 0),
            Arg.Any<CancellationToken>());

        // Verify: Event published
        await eventBus.Received(1).PublishAsync(
            Arg.Is<OrderPlacedPayload>(p =>
                p.TraderId == TraderId && p.ExchangeId == ExchangeId &&
                p.StrategyId == BindingId && p.Pair == "BTCUSDT" &&
                p.Side == "Buy" && p.Type == "Market"),
            Arg.Any<CancellationToken>());
        await consumer.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Trade 事件触发了评估但风控拒绝 → 不下单。
    /// </summary>
    [Fact]
    public async Task TradeEvent_RiskCheckDenied_NoOrderPlaced()
    {
        var indRegistry = new IndicatorRegistry();
        indRegistry.Register("RSI", _ => 40m);

        var (consumer, services, tradeChannel, klineChannel, eventBus, tradeExecutor, riskManager) =
            BuildConsumer(indRegistry);

        // Risk check returns denied
        riskManager.CheckAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new RiskResult(false, ["当日亏损超限"]));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await consumer.StartAsync(cts.Token);
        await Task.Delay(500);

        var firstTradeEvent = new TradeEvent(
            "BTCUSDT", ExchangeType.Binance, ExchangeId,
            new Trade(DateTime.UtcNow.AddSeconds(-1), 49900m, 0.1m, IsBuyerMaker: false));
        var secondTradeEvent = new TradeEvent(
            "BTCUSDT", ExchangeType.Binance, ExchangeId,
            new Trade(DateTime.UtcNow, 50000m, 0.1m, IsBuyerMaker: false));
        await tradeChannel.Writer.WriteAsync(firstTradeEvent, cts.Token);
        await tradeChannel.Writer.WriteAsync(secondTradeEvent, cts.Token);
        await Task.Delay(1000);

        // No order should be placed
        await tradeExecutor.DidNotReceiveWithAnyArgs().ExecuteMarketOrderAsync(default!);

        // Risk alert should be published
        await eventBus.Received(1).PublishAsync(
            Arg.Is<RiskAlertPayload>(p =>
                p.TraderId == TraderId && p.Level == "Warning" &&
                p.Category == "RiskCheck" && p.StrategyId == BindingId &&
                p.Message.Contains("当日亏损")),
            Arg.Any<CancellationToken>());
        await consumer.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Kline 事件触发策略评估。
    /// </summary>
    [Fact]
    public async Task KlineEvent_TriggerEvaluation_PlacesOrder()
    {
        var indRegistry = new IndicatorRegistry();
        indRegistry.Register("RSI", _ => 40m); // RSI > 30 → enters

        var (consumer, services, tradeChannel, klineChannel, eventBus, tradeExecutor, riskManager) =
            BuildConsumer(indRegistry, timeframe: "15m");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await consumer.StartAsync(cts.Token);
        await Task.Delay(500);

        var candle = new Kline(DateTime.UtcNow, 49500m, 50200m, 49400m, 50000m, 1000);
        var klineEvent = new KlineEvent(
            "BTCUSDT", ExchangeType.Binance, ExchangeId, "15m", candle);
        await klineChannel.Writer.WriteAsync(klineEvent, cts.Token);
        await Task.Delay(1000);

        await tradeExecutor.Received(1).ExecuteMarketOrderAsync(
            Arg.Is<Order>(o => o.Pair == "BTCUSDT"),
            Arg.Any<CancellationToken>());
        await consumer.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// 没有匹配的策略时，事件不应触发任何操作。
    /// </summary>
    [Fact]
    public async Task TradeEvent_NoMatchingBinding_NoOp()
    {
        // Binding with no pair match
        var binding = new StrategyBinding
        {
            Id = BindingId,
            TraderId = TraderId,
            ExchangeId = ExchangeId,
            Pairs = "ETHUSDT", // Trade event is for BTCUSDT
            StrategyId = StrategyId,
            Status = BindingStatus.Active
        };

        var (consumer, services, tradeChannel, klineChannel, eventBus, tradeExecutor, riskManager) =
            BuildConsumer(new IndicatorRegistry(), extraBindings: [binding]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await consumer.StartAsync(cts.Token);
        await Task.Delay(500);

        var tradeEvent = new TradeEvent(
            "BTCUSDT", ExchangeType.Binance, ExchangeId,
            new Trade(DateTime.UtcNow, 50000m, 0.1m, IsBuyerMaker: false));
        await tradeChannel.Writer.WriteAsync(tradeEvent, cts.Token);
        await Task.Delay(1000);

        await tradeExecutor.DidNotReceiveWithAnyArgs().ExecuteMarketOrderAsync(default!);
        await consumer.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// 入场条件不满足 → 不下单。
    /// </summary>
    [Fact]
    public async Task TradeEvent_EntryConditionNotMet_NoOrder()
    {
        var indRegistry = new IndicatorRegistry();
        indRegistry.Register("RSI", _ => 20m); // RSI < 30 → condition not met

        var (consumer, services, tradeChannel, klineChannel, eventBus, tradeExecutor, riskManager) =
            BuildConsumer(indRegistry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await consumer.StartAsync(cts.Token);
        await Task.Delay(500);

        var tradeEvent = new TradeEvent(
            "BTCUSDT", ExchangeType.Binance, ExchangeId,
            new Trade(DateTime.UtcNow, 50000m, 0.1m, IsBuyerMaker: false));
        await tradeChannel.Writer.WriteAsync(tradeEvent, cts.Token);
        await Task.Delay(1000);

        await tradeExecutor.DidNotReceiveWithAnyArgs().ExecuteMarketOrderAsync(default!);
        await consumer.StopAsync(CancellationToken.None);
    }

    // ─────────────── Test Infrastructure ───────────────

    private static readonly Strategy DefaultStrategy = new()
    {
        Id = StrategyId,
        Name = "Test Strategy",
        ExecutionRule = """{"code":"test","name":"测试策略","rules":[{"code":"entry","name":"恒真入场","when":{"operator":"TRUE"},"then":{"action":"buy","size":100,"sizeType":"fixed"}}]}"""
    };

    private static readonly StrategyBinding DefaultBinding = new()
    {
        Id = BindingId,
        TraderId = TraderId,
        ExchangeId = ExchangeId,
        Pairs = "BTCUSDT",
        Timeframe = "15m",
        StrategyId = StrategyId,
        Status = BindingStatus.Active
    };

    private static (StrategyEvaluationConsumer, ServiceProvider, Channel<TradeEvent>, Channel<KlineEvent>,
        IDomainEventBus, ITradeExecutor, IPortfolioRiskManager) BuildConsumer(
        IIndicatorRegistry indRegistry,
        List<StrategyBinding>? extraBindings = null,
        string? timeframe = null)
    {
        var tradeChannel = Channel.CreateBounded<TradeEvent>(new BoundedChannelOptions(100)
        { FullMode = BoundedChannelFullMode.DropOldest });
        var klineChannel = Channel.CreateBounded<KlineEvent>(new BoundedChannelOptions(100)
        { FullMode = BoundedChannelFullMode.DropOldest });

        var eventBus = Substitute.For<IDomainEventBus>();
        var tradeExecutor = Substitute.For<ITradeExecutor>();
        var riskManager = Substitute.For<IPortfolioRiskManager>();

        // Default: risk allows, trade succeeds
        riskManager.CheckAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new RiskResult(true, []));
        riskManager.CheckPairRiskAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(new RiskResult(true, []));
        tradeExecutor.ExecuteMarketOrderAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(new OrderResult(true, "exch-123", 0.001m, 50000m, 0m, null));

        var logger = Substitute.For<ILogger<StrategyEvaluationConsumer>>();

        // Build DI scope with all services
        var allBindings = new List<StrategyBinding> { DefaultBinding };
        if (extraBindings is not null)
            allBindings.AddRange(extraBindings);

        // Apply timeframe overrides
        var testBindings = allBindings.Select(b => new StrategyBinding
        {
            Id = b.Id,
            TraderId = b.TraderId,
            ExchangeId = b.ExchangeId,
            Pairs = b.Pairs,
            Timeframe = timeframe ?? b.Timeframe,
            StrategyId = b.StrategyId,
            Status = b.Status
        }).ToList();

        var bindingRepo = Substitute.For<IStrategyBindingRepository>();
        bindingRepo.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(testBindings);

        var strategyRepo = Substitute.For<IStrategyRepository>();
        strategyRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(DefaultStrategy);

        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TradeX.Core.Models.Exchange { Id = ExchangeId, Type = ExchangeType.Binance, ApiKeyEncrypted = "enc", SecretKeyEncrypted = "enc" });

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.SubscribeTradesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<Trade>());
        exchangeClient.SubscribeKlinesStreamAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<Kline>());
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(exchangeClient);

        // 入场幂等闸依赖 OrderRepo.HasActiveBuyAsync —— 默认无在途买单，不阻断下单。
        var orderRepo = Substitute.For<IOrderRepository>();
        orderRepo.HasActiveBuyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var positionRepo = Substitute.For<IPositionRepository>();
        positionRepo.GetByStrategyIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        positionRepo.GetOpenByTraderIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        positionRepo.GetClosedByTraderIdSinceAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        // Register all services in DI
        var services = new ServiceCollection();
        services.AddSingleton(bindingRepo);
        services.AddSingleton(strategyRepo);
        services.AddSingleton(exchangeRepo);
        services.AddSingleton(clientFactory);
        services.AddSingleton(orderRepo);
        services.AddSingleton(positionRepo);
        services.AddSingleton(eventBus);
        services.AddSingleton(tradeExecutor);
        services.AddSingleton(riskManager);
        services.AddSingleton(indRegistry);

        // 注册规则引擎服务
        services.AddSingleton<ITriggerTracker, TriggerTracker>();
        services.AddSingleton<IRuleEvaluator, RuleEvaluator>();
        services.AddSingleton<IStrategyDecisionEngine, StrategyDecisionEngine>();

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var tradeLogger = Substitute.For<ILogger<TradeStreamManager>>();
        var klineLogger = Substitute.For<ILogger<KlineStreamManager>>();

        var tradeManager = new TradeStreamManager(scopeFactory, tradeChannel, tradeLogger);
        var klineManager = new KlineStreamManager(scopeFactory, klineChannel, klineLogger);
        var metrics = new TradeXMetrics();

        var consumer = new StrategyEvaluationConsumer(
            scopeFactory, tradeChannel, klineChannel, indRegistry, eventBus,
            metrics, tradeManager, klineManager, new SystemClock(), logger);

        return (consumer, sp, tradeChannel, klineChannel, eventBus, tradeExecutor, riskManager);
    }
}
