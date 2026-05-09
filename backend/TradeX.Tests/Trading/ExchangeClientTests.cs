using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Exchange;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class ExchangeClientFactoryTests
{
    private readonly ExchangeClientFactory _factory = new();

    [Fact]
    public void CreateClient_Binance_ReturnsBinanceClient()
    {
        var client = _factory.CreateClient(ExchangeType.Binance, "key", "secret");
        Assert.IsType<BinanceClient>(client);
    }

    [Fact]
    public void CreateClient_Bybit_ReturnsBybitClient()
    {
        var client = _factory.CreateClient(ExchangeType.Bybit, "key", "secret");
        Assert.IsType<BybitClient>(client);
    }

    [Fact]
    public void CreateClient_OKX_ReturnsOkxClient()
    {
        var client = _factory.CreateClient(ExchangeType.OKX, "key", "secret", "pass");
        Assert.IsType<OkxClient>(client);
    }

    [Fact]
    public void CreateClient_Gate_ReturnsGateIoClient()
    {
        var client = _factory.CreateClient(ExchangeType.Gate, "key", "secret");
        Assert.IsType<GateIoClient>(client);
    }

    [Fact]
    public void CreateClient_HTX_ReturnsHtxClient()
    {
        var client = _factory.CreateClient(ExchangeType.HTX, "key", "secret");
        Assert.IsType<HtxClient>(client);
    }

    [Fact]
    public void CreateClient_UnknownType_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _factory.CreateClient((ExchangeType)999, "key", "secret"));
    }

    [Fact]
    public void AllClients_HaveCorrectType()
    {
        Assert.Equal(ExchangeType.Binance, new BinanceClient("k", "s", false).Type);
        Assert.Equal(ExchangeType.Bybit, new BybitClient("k", "s", false).Type);
        Assert.Equal(ExchangeType.OKX, new OkxClient("k", "s").Type);
        Assert.Equal(ExchangeType.Gate, new GateIoClient("k", "s").Type);
        Assert.Equal(ExchangeType.HTX, new HtxClient("k", "s").Type);
    }
}

public class TradeExecutorTests
{
    [Fact]
    public async Task ExecuteMarketOrderAsync_MissingAccount_ReturnsError()
    {
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TradeX.Core.Models.Exchange?>(null));

        var executor = new TradeExecutor(
            Substitute.For<IExchangeClientFactory>(),
            exchangeRepo,
            Substitute.For<IEncryptionService>(),
            Substitute.For<ILogger<TradeExecutor>>());

        var result = await executor.ExecuteMarketOrderAsync(new Order { ExchangeId = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains("交易所不存在", result.Error);
    }

    [Fact]
    public async Task ExecuteLimitOrderAsync_NoPrice_ReturnsError()
    {
        var executor = new TradeExecutor(
            Substitute.For<IExchangeClientFactory>(),
            Substitute.For<IExchangeRepository>(),
            Substitute.For<IEncryptionService>(),
            Substitute.For<ILogger<TradeExecutor>>());

        var result = await executor.ExecuteLimitOrderAsync(new Order { ExchangeId = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains("必须指定价格", result.Error);
    }

    [Fact]
    public async Task ExecuteStopLimitOrderAsync_NoPrice_ReturnsError()
    {
        var executor = new TradeExecutor(
            Substitute.For<IExchangeClientFactory>(),
            Substitute.For<IExchangeRepository>(),
            Substitute.For<IEncryptionService>(),
            Substitute.For<ILogger<TradeExecutor>>());

        var result = await executor.ExecuteStopLimitOrderAsync(new Order { ExchangeId = Guid.NewGuid() }, 100);

        Assert.False(result.Success);
        Assert.Contains("必须指定价格", result.Error);
    }

    [Fact]
    public async Task ExecuteMarketOrderAsync_WithAccount_CallsPlaceOrder()
    {
        var exchange = new TradeX.Core.Models.Exchange
        {
            Id = Guid.NewGuid(),
            Type = ExchangeType.Binance,
            ApiKeyEncrypted = "enc-key",
            SecretKeyEncrypted = "enc-secret"
        };

        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(exchange.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TradeX.Core.Models.Exchange?>(exchange));

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("enc-key").Returns("key");
        encryption.Decrypt("enc-secret").Returns("secret");

        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.PlaceOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OrderResult(true, "123", 1, 50000, 0, null));

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(exchangeClient);

        var executor = new TradeExecutor(
            clientFactory, exchangeRepo, encryption,
            Substitute.For<ILogger<TradeExecutor>>());

        var order = new Order
        {
            ExchangeId = exchange.Id,
            Pair = "BTCUSDT",
            Side = OrderSide.Buy,
            Quantity = 1,
            QuoteQuantity = 100
        };

        var result = await executor.ExecuteMarketOrderAsync(order);

        Assert.True(result.Success);
        await exchangeClient.Received(1).PlaceOrderAsync(
            Arg.Is<OrderRequest>(r => r.Pair == "BTCUSDT" && r.Type == OrderType.Market),
            Arg.Any<CancellationToken>());
    }
}
