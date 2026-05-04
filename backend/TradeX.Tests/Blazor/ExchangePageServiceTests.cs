using NSubstitute;
using TradeX.Blazor.Services;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Tests.Blazor;

public class ExchangePageServiceTests
{
    [Fact]
    public async Task GetOrdersAsync_History_LoadsOrderHistory()
    {
        var exchangeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var exchange = new TradeX.Core.Models.Exchange
        {
            Id = exchangeId,
            CreatedBy = userId,
            Name = "Binance",
            Type = ExchangeType.Binance,
            Status = ExchangeStatus.Enabled,
            ApiKeyEncrypted = "api",
            SecretKeyEncrypted = "secret"
        };

        var expected = new ExchangeOrderDto(
            "BTCUSDT",
            "Buy",
            "Limit",
            "Filled",
            50000,
            0.1m,
            0.1m,
            "order-1",
            DateTime.UtcNow);

        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns(exchange);

        var traderRepo = Substitute.For<ITraderRepository>();
        var clientFactory = Substitute.For<IExchangeClientFactory>();
        var exchangeClient = Substitute.For<IExchangeClient>();
        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("api").Returns("api-key");
        encryption.Decrypt("secret").Returns("secret-key");
        exchangeClient.GetOrderHistoryAsync(Arg.Any<CancellationToken>())
            .Returns([expected]);
        clientFactory.CreateClient(ExchangeType.Binance, "api-key", "secret-key", null)
            .Returns(exchangeClient);

        var service = new ExchangePageService(exchangeRepo, traderRepo, clientFactory, encryption);

        var orders = await service.GetOrdersAsync(exchangeId, OrderListType.History);

        Assert.Single(orders);
        Assert.Equal("order-1", orders[0].ExchangeOrderId);
        await exchangeClient.Received(1).GetOrderHistoryAsync(Arg.Any<CancellationToken>());
        await exchangeClient.DidNotReceive().GetOpenOrdersAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrdersAsync_Open_LoadsOpenOrders()
    {
        var exchangeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var exchange = new TradeX.Core.Models.Exchange
        {
            Id = exchangeId,
            CreatedBy = userId,
            Name = "Binance",
            Type = ExchangeType.Binance,
            Status = ExchangeStatus.Enabled,
            ApiKeyEncrypted = "api",
            SecretKeyEncrypted = "secret"
        };

        var expected = new ExchangeOrderDto(
            "ETHUSDT",
            "Sell",
            "Limit",
            "New",
            3000,
            1m,
            0m,
            "order-2",
            DateTime.UtcNow);

        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns(exchange);

        var traderRepo = Substitute.For<ITraderRepository>();
        var clientFactory = Substitute.For<IExchangeClientFactory>();
        var exchangeClient = Substitute.For<IExchangeClient>();
        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("api").Returns("api-key");
        encryption.Decrypt("secret").Returns("secret-key");
        exchangeClient.GetOpenOrdersAsync(Arg.Any<CancellationToken>())
            .Returns([expected]);
        clientFactory.CreateClient(ExchangeType.Binance, "api-key", "secret-key", null)
            .Returns(exchangeClient);

        var service = new ExchangePageService(exchangeRepo, traderRepo, clientFactory, encryption);

        var orders = await service.GetOrdersAsync(exchangeId, OrderListType.Open);

        Assert.Single(orders);
        Assert.Equal("order-2", orders[0].ExchangeOrderId);
        await exchangeClient.Received(1).GetOpenOrdersAsync(Arg.Any<CancellationToken>());
        await exchangeClient.DidNotReceive().GetOrderHistoryAsync(Arg.Any<CancellationToken>());
    }
}
