using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class OrderReconcilerTests
{
    private static (OrderReconciler reconciler, IExchangeRepository exchangeRepo, IOrderRepository orderRepo,
        IExchangeClient exchangeClient, IExchangeClientFactory clientFactory) BuildReconciler(int stalePendingMinutes = 5)
    {
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        var orderRepo = Substitute.For<IOrderRepository>();
        var clientFactory = Substitute.For<IExchangeClientFactory>();
        var exchangeClient = Substitute.For<IExchangeClient>();
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(exchangeClient);
        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt(Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0) + "-dec");
        var settings = Options.Create(new RiskSettings { StalePendingMinutes = stalePendingMinutes });
        var logger = Substitute.For<ILogger<OrderReconciler>>();
        var reconciler = new OrderReconciler(exchangeRepo, orderRepo, clientFactory, encryption, settings, logger);
        return (reconciler, exchangeRepo, orderRepo, exchangeClient, clientFactory);
    }

    private static TradeX.Core.Models.Exchange MakeExchange(Guid id) => new()
    {
        Id = id,
        Status = ExchangeStatus.Enabled,
        Type = ExchangeType.Binance,
        ApiKeyEncrypted = "k",
        SecretKeyEncrypted = "s"
    };

    [Fact]
    public async Task ReconcileAsync_StalePendingWithoutExchangeOrderId_MarksFailed()
    {
        var (rec, exchangeRepo, orderRepo, _, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);

        var stale = new Order
        {
            Id = Guid.NewGuid(), ExchangeId = exchangeId,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([stale]);

        await rec.ReconcileAsync();

        Assert.Equal(OrderStatus.Failed, stale.Status);
        await orderRepo.Received(1).UpdateAsync(stale, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_RecentPending_DoesNotChange()
    {
        var (rec, exchangeRepo, orderRepo, _, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);
        var recent = new Order
        {
            Id = Guid.NewGuid(), ExchangeId = exchangeId,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([recent]);

        await rec.ReconcileAsync();

        Assert.Equal(OrderStatus.Pending, recent.Status);
        await orderRepo.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_NoEnabledExchanges_DoesNothing()
    {
        var (rec, exchangeRepo, orderRepo, _, _) = BuildReconciler();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>()).Returns([]);

        await rec.ReconcileAsync();

        await orderRepo.DidNotReceive().GetPendingByExchangeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_NoPendingOrders_DoesNothing()
    {
        var (rec, exchangeRepo, orderRepo, _, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([]);

        await rec.ReconcileAsync();

        await orderRepo.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_HasExchangeOrderId_FilledOnExchange_MarksFilled()
    {
        var (rec, exchangeRepo, orderRepo, exchangeClient, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);

        var order = new Order
        {
            Id = Guid.NewGuid(), ExchangeId = exchangeId,
            ExchangeOrderId = "EX-42",
            Quantity = 1m,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-3)  // 不到陈旧阈值，但有 ExchangeOrderId 仍会查
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([order]);
        exchangeClient.GetOrderAsync(Arg.Any<string>(), "EX-42", Arg.Any<CancellationToken>())
            .Returns(new OrderResult(true, "EX-42", FilledQuantity: 1m, AvgPrice: 50000m, Fee: 0.05m, Error: null));

        await rec.ReconcileAsync();

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(1m, order.FilledQuantity);
        Assert.Equal(0.05m, order.Fee);
        await orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_HasExchangeOrderId_PartialFilled_MarksPartiallyFilled()
    {
        var (rec, exchangeRepo, orderRepo, exchangeClient, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);

        var order = new Order
        {
            Id = Guid.NewGuid(), ExchangeId = exchangeId,
            ExchangeOrderId = "EX-43",
            Quantity = 2m,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-3)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([order]);
        exchangeClient.GetOrderAsync(Arg.Any<string>(), "EX-43", Arg.Any<CancellationToken>())
            .Returns(new OrderResult(true, "EX-43", FilledQuantity: 0.8m, AvgPrice: 100m, Fee: 0.001m, Error: null));

        await rec.ReconcileAsync();

        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
        Assert.Equal(0.8m, order.FilledQuantity);
        await orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_HasExchangeOrderId_ExchangeUnknown_AndStale_MarksFailed()
    {
        var (rec, exchangeRepo, orderRepo, exchangeClient, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);

        var order = new Order
        {
            Id = Guid.NewGuid(), ExchangeId = exchangeId,
            ExchangeOrderId = "EX-orphan",
            Quantity = 1m,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([order]);
        exchangeClient.GetOrderAsync(Arg.Any<string>(), "EX-orphan", Arg.Any<CancellationToken>())
            .Returns(new OrderResult(false, null, 0, 0, 0, "order not found"));

        await rec.ReconcileAsync();

        Assert.Equal(OrderStatus.Failed, order.Status);
        await orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_NoExchangeOrderId_ClientOrderIdLookupSucceeds_RecoversOrder()
    {
        var (rec, exchangeRepo, orderRepo, exchangeClient, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);

        var clientOrderId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(), ExchangeId = exchangeId,
            ClientOrderId = clientOrderId,
            Pair = "BTCUSDT",
            ExchangeOrderId = null,
            Quantity = 1m,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-1)  // 不到陈旧阈值，但 ClientOrderId 反查成功照样修复
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([order]);
        exchangeClient.GetOrderByClientOrderIdAsync("BTCUSDT", clientOrderId.ToString("N"), Arg.Any<CancellationToken>())
            .Returns(new OrderResult(true, "RECOVERED-99", FilledQuantity: 1m, AvgPrice: 50000m, Fee: 0.05m, Error: null));

        await rec.ReconcileAsync();

        Assert.Equal("RECOVERED-99", order.ExchangeOrderId);
        Assert.Equal(OrderStatus.Filled, order.Status);
        await orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_NoExchangeOrderId_ClientOrderIdLookupNotSupported_StaleMarksFailed()
    {
        var (rec, exchangeRepo, orderRepo, exchangeClient, _) = BuildReconciler();
        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(exchangeId)]);

        var order = new Order
        {
            Id = Guid.NewGuid(), ExchangeId = exchangeId,
            Pair = "BTCUSDT",
            ExchangeOrderId = null,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([order]);
        exchangeClient.GetOrderByClientOrderIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OrderResult(false, null, 0, 0, 0, "not_supported"));

        await rec.ReconcileAsync();

        Assert.Equal(OrderStatus.Failed, order.Status);
        await orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_MultipleExchanges_ProcessesEach()
    {
        var (rec, exchangeRepo, orderRepo, _, _) = BuildReconciler();
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([MakeExchange(e1), MakeExchange(e2)]);

        var staleA = new Order { Id = Guid.NewGuid(), ExchangeId = e1, Status = OrderStatus.Pending, PlacedAtUtc = DateTime.UtcNow.AddMinutes(-10) };
        var staleB = new Order { Id = Guid.NewGuid(), ExchangeId = e2, Status = OrderStatus.Pending, PlacedAtUtc = DateTime.UtcNow.AddMinutes(-6) };
        orderRepo.GetPendingByExchangeAsync(e1, Arg.Any<CancellationToken>()).Returns([staleA]);
        orderRepo.GetPendingByExchangeAsync(e2, Arg.Any<CancellationToken>()).Returns([staleB]);

        await rec.ReconcileAsync();

        Assert.Equal(OrderStatus.Failed, staleA.Status);
        Assert.Equal(OrderStatus.Failed, staleB.Status);
        await orderRepo.Received(2).UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
