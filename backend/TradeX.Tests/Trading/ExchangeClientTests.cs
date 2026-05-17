using System.Reflection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Exchange;
using TradeX.Exchange.Adapters;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class ExchangeClientFactoryTests
{
    private readonly ExchangeClientFactory _factory = new();

    [Fact]
    public void CreateClient_Binance_ReturnsBinanceClientAdapter()
    {
        var client = _factory.CreateClient(ExchangeType.Binance, "key", "secret");
        Assert.IsType<BinanceClientAdapter>(client);
    }

    [Fact]
    public void CreateClient_Bybit_ReturnsBybitClientAdapter()
    {
        var client = _factory.CreateClient(ExchangeType.Bybit, "key", "secret");
        Assert.IsType<BybitClientAdapter>(client);
    }

    [Fact]
    public void CreateClient_OKX_ReturnsOkxClientAdapter()
    {
        var client = _factory.CreateClient(ExchangeType.OKX, "key", "secret", "pass");
        Assert.IsType<OkxClientAdapter>(client);
    }

    [Fact]
    public void CreateClient_Gate_ReturnsGateIoClientAdapter()
    {
        var client = _factory.CreateClient(ExchangeType.Gate, "key", "secret");
        Assert.IsType<GateIoClientAdapter>(client);
    }

    [Fact]
    public void CreateClient_HTX_ReturnsHtxClientAdapter()
    {
        var client = _factory.CreateClient(ExchangeType.HTX, "key", "secret");
        Assert.IsType<HtxClientAdapter>(client);
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
        Assert.Equal(ExchangeType.Binance, new BinanceClientAdapter("k", "s", false).Type);
        Assert.Equal(ExchangeType.Bybit, new BybitClientAdapter("k", "s", false).Type);
        Assert.Equal(ExchangeType.OKX, new OkxClientAdapter("k", "s", null).Type);
        Assert.Equal(ExchangeType.Gate, new GateIoClientAdapter("k", "s").Type);
        Assert.Equal(ExchangeType.HTX, new HtxClientAdapter("k", "s").Type);
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
            Substitute.For<IOrderRepository>(),
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
            Substitute.For<IOrderRepository>(),
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
            Substitute.For<IOrderRepository>(),
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
            clientFactory, exchangeRepo, Substitute.For<IOrderRepository>(), encryption,
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

    [Fact]
    public async Task ExecuteMarketOrderAsync_PrePersistsBeforeExchangeCall()
    {
        var exchange = new TradeX.Core.Models.Exchange
        {
            Id = Guid.NewGuid(),
            Type = ExchangeType.Binance,
            ApiKeyEncrypted = "k",
            SecretKeyEncrypted = "s"
        };
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(exchange.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TradeX.Core.Models.Exchange?>(exchange));

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt(Arg.Any<string>()).Returns("decrypted");

        var orderRepo = Substitute.For<IOrderRepository>();
        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.PlaceOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OrderResult(true, "EX-1", 1m, 50000m, 0.05m, null));

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(exchangeClient);

        var executor = new TradeExecutor(clientFactory, exchangeRepo, orderRepo, encryption,
            Substitute.For<ILogger<TradeExecutor>>());

        var order = new Order
        {
            ExchangeId = exchange.Id, Pair = "BTCUSDT",
            Side = OrderSide.Buy, Quantity = 1, QuoteQuantity = 100
        };

        OrderStatus? statusAtAdd = null;
        orderRepo.AddAsync(Arg.Do<Order>(o => statusAtAdd = o.Status), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await executor.ExecuteMarketOrderAsync(order);

        // pre-persist 时订单还是 Pending
        Assert.Equal(OrderStatus.Pending, statusAtAdd);
        // post-update 阶段把订单更新为 Filled，带 ExchangeOrderId
        await orderRepo.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await orderRepo.Received(1).UpdateAsync(
            Arg.Is<Order>(o => o.Status == OrderStatus.Filled && o.ExchangeOrderId == "EX-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteMarketOrderAsync_PassesClientOrderIdToExchange()
    {
        var exchange = new TradeX.Core.Models.Exchange
        {
            Id = Guid.NewGuid(), Type = ExchangeType.Binance,
            ApiKeyEncrypted = "k", SecretKeyEncrypted = "s"
        };
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(exchange.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TradeX.Core.Models.Exchange?>(exchange));

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt(Arg.Any<string>()).Returns("decrypted");

        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.PlaceOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OrderResult(true, "EX-2", 1m, 50000m, 0, null));

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(exchangeClient);

        var executor = new TradeExecutor(clientFactory, exchangeRepo, Substitute.For<IOrderRepository>(),
            encryption, Substitute.For<ILogger<TradeExecutor>>());

        var clientOrderId = Guid.NewGuid();
        var order = new Order
        {
            ExchangeId = exchange.Id, ClientOrderId = clientOrderId, Pair = "BTCUSDT",
            Side = OrderSide.Buy, Quantity = 1, QuoteQuantity = 100
        };

        await executor.ExecuteMarketOrderAsync(order);

        await exchangeClient.Received(1).PlaceOrderAsync(
            Arg.Is<OrderRequest>(r => r.ClientOrderId == clientOrderId.ToString("N")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteMarketOrderAsync_ExchangeFailure_MarksOrderFailed()
    {
        var exchange = new TradeX.Core.Models.Exchange
        {
            Id = Guid.NewGuid(), Type = ExchangeType.Binance,
            ApiKeyEncrypted = "k", SecretKeyEncrypted = "s"
        };
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(exchange.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TradeX.Core.Models.Exchange?>(exchange));

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt(Arg.Any<string>()).Returns("decrypted");

        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.PlaceOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OrderResult(false, null, 0, 0, 0, "insufficient balance"));

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(exchangeClient);

        var orderRepo = Substitute.For<IOrderRepository>();
        var executor = new TradeExecutor(clientFactory, exchangeRepo, orderRepo, encryption,
            Substitute.For<ILogger<TradeExecutor>>());

        var order = new Order
        {
            ExchangeId = exchange.Id, Pair = "BTCUSDT",
            Side = OrderSide.Buy, Quantity = 1, QuoteQuantity = 100
        };

        var result = await executor.ExecuteMarketOrderAsync(order);

        Assert.False(result.Success);
        await orderRepo.Received(1).UpdateAsync(Arg.Is<Order>(o => o.Status == OrderStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteMarketOrderAsync_ExchangeThrows_KeepsOrderPending()
    {
        var exchange = new TradeX.Core.Models.Exchange
        {
            Id = Guid.NewGuid(), Type = ExchangeType.Binance,
            ApiKeyEncrypted = "k", SecretKeyEncrypted = "s"
        };
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(exchange.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TradeX.Core.Models.Exchange?>(exchange));

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt(Arg.Any<string>()).Returns("decrypted");

        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.PlaceOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns<OrderResult>(_ => throw new HttpRequestException("network error"));

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(exchangeClient);

        var orderRepo = Substitute.For<IOrderRepository>();
        var executor = new TradeExecutor(clientFactory, exchangeRepo, orderRepo, encryption,
            Substitute.For<ILogger<TradeExecutor>>());

        var order = new Order
        {
            ExchangeId = exchange.Id, Pair = "BTCUSDT",
            Side = OrderSide.Buy, Quantity = 1, QuoteQuantity = 100
        };

        var result = await executor.ExecuteMarketOrderAsync(order);

        Assert.False(result.Success);
        // Pending 保留：交易所是否实际收到未知，留给对账器处理
        await orderRepo.Received(1).AddAsync(Arg.Is<Order>(o => o.Status == OrderStatus.Pending), Arg.Any<CancellationToken>());
        await orderRepo.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
