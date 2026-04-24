using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class OrderReconcilerTests
{
    [Fact]
    public async Task ReconcileAsync_PendingOrderOlderThan5Minutes_MarksFailed()
    {
        var exchangeRepo = Substitute.For<IExchangeAccountRepository>();
        var orderRepo = Substitute.For<IOrderRepository>();
        var logger = Substitute.For<ILogger<OrderReconciler>>();
        var reconciler = new OrderReconciler(exchangeRepo, orderRepo, logger);

        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([new ExchangeAccount { Id = exchangeId, Status = ExchangeAccountStatus.Enabled }]);

        var staleOrder = new Order
        {
            Id = Guid.NewGuid(),
            ExchangeId = exchangeId,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([staleOrder]);

        await reconciler.ReconcileAsync();

        Assert.Equal(OrderStatus.Failed, staleOrder.Status);
        await orderRepo.Received(1).UpdateAsync(staleOrder, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_RecentPendingOrder_DoesNotChange()
    {
        var exchangeRepo = Substitute.For<IExchangeAccountRepository>();
        var orderRepo = Substitute.For<IOrderRepository>();
        var logger = Substitute.For<ILogger<OrderReconciler>>();
        var reconciler = new OrderReconciler(exchangeRepo, orderRepo, logger);

        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([new ExchangeAccount { Id = exchangeId, Status = ExchangeAccountStatus.Enabled }]);

        var recentOrder = new Order
        {
            Id = Guid.NewGuid(),
            ExchangeId = exchangeId,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([recentOrder]);

        await reconciler.ReconcileAsync();

        Assert.Equal(OrderStatus.Pending, recentOrder.Status);
        await orderRepo.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_NoPendingOrders_DoesNothing()
    {
        var exchangeRepo = Substitute.For<IExchangeAccountRepository>();
        var orderRepo = Substitute.For<IOrderRepository>();
        var logger = Substitute.For<ILogger<OrderReconciler>>();
        var reconciler = new OrderReconciler(exchangeRepo, orderRepo, logger);

        var exchangeId = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([new ExchangeAccount { Id = exchangeId, Status = ExchangeAccountStatus.Enabled }]);
        orderRepo.GetPendingByExchangeAsync(exchangeId, Arg.Any<CancellationToken>())
            .Returns([]);

        await reconciler.ReconcileAsync();

        await orderRepo.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_MultipleExchanges_ProcessesEach()
    {
        var exchangeRepo = Substitute.For<IExchangeAccountRepository>();
        var orderRepo = Substitute.For<IOrderRepository>();
        var logger = Substitute.For<ILogger<OrderReconciler>>();
        var reconciler = new OrderReconciler(exchangeRepo, orderRepo, logger);

        var exchangeId1 = Guid.NewGuid();
        var exchangeId2 = Guid.NewGuid();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([
                new ExchangeAccount { Id = exchangeId1, Status = ExchangeAccountStatus.Enabled },
                new ExchangeAccount { Id = exchangeId2, Status = ExchangeAccountStatus.Enabled }
            ]);

        var staleOrder1 = new Order
        {
            Id = Guid.NewGuid(),
            ExchangeId = exchangeId1,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        var staleOrder2 = new Order
        {
            Id = Guid.NewGuid(),
            ExchangeId = exchangeId2,
            Status = OrderStatus.Pending,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-6)
        };
        orderRepo.GetPendingByExchangeAsync(exchangeId1, Arg.Any<CancellationToken>())
            .Returns([staleOrder1]);
        orderRepo.GetPendingByExchangeAsync(exchangeId2, Arg.Any<CancellationToken>())
            .Returns([staleOrder2]);

        await reconciler.ReconcileAsync();

        Assert.Equal(OrderStatus.Failed, staleOrder1.Status);
        Assert.Equal(OrderStatus.Failed, staleOrder2.Status);
        await orderRepo.Received(2).UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_NoEnabledExchanges_DoesNothing()
    {
        var exchangeRepo = Substitute.For<IExchangeAccountRepository>();
        var orderRepo = Substitute.For<IOrderRepository>();
        var logger = Substitute.For<ILogger<OrderReconciler>>();
        var reconciler = new OrderReconciler(exchangeRepo, orderRepo, logger);

        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        await reconciler.ReconcileAsync();

        await orderRepo.DidNotReceive().GetPendingByExchangeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
