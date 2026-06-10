using Microsoft.EntityFrameworkCore;
using TradeX.Core.Enums;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data;
using TradeX.Infrastructure.Data.Repositories;

namespace TradeX.Tests.Infrastructure;

/// <summary>
/// 覆盖订单幂等闸所依赖的"在途订单"查询：
///   * HasActiveBuyAsync —— 入场幂等闸跨重启兜底
///   * HasActiveSellAsync —— 平仓幂等闸跨重启兜底（仅匹配该持仓的 Sell + Pending/PartiallyFilled）
/// </summary>
public class OrderRepositoryTests
{
    private static DbContextOptions<TradeXDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<TradeXDbContext>()
            .UseInMemoryDatabase(dbName)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

    private static Order Sell(Guid positionId, OrderStatus status) => new()
    {
        ExchangeId = Guid.NewGuid(),
        PositionId = positionId,
        Pair = "BTCUSDT",
        Side = OrderSide.Sell,
        Type = OrderType.Market,
        Status = status,
        Quantity = 1m
    };

    [Theory]
    [InlineData(OrderStatus.Pending, true)]
    [InlineData(OrderStatus.PartiallyFilled, true)]
    [InlineData(OrderStatus.Filled, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Failed, false)]
    public async Task HasActiveSellAsync_MatchesOnlyInFlightSellForPosition(OrderStatus status, bool expected)
    {
        var positionId = Guid.NewGuid();
        await using var ctx = new TradeXDbContext(Options(Guid.NewGuid().ToString()));
        var repo = new OrderRepository(ctx);
        await repo.AddAsync(Sell(positionId, status));

        Assert.Equal(expected, await repo.HasActiveSellAsync(positionId));
    }

    [Fact]
    public async Task HasActiveSellAsync_IgnoresOtherPositionsAndBuySide()
    {
        var positionId = Guid.NewGuid();
        await using var ctx = new TradeXDbContext(Options(Guid.NewGuid().ToString()));
        var repo = new OrderRepository(ctx);

        // 别的持仓的在途卖单
        await repo.AddAsync(Sell(Guid.NewGuid(), OrderStatus.Pending));
        // 同持仓但是买单
        var buy = Sell(positionId, OrderStatus.Pending);
        buy.Side = OrderSide.Buy;
        await repo.AddAsync(buy);

        Assert.False(await repo.HasActiveSellAsync(positionId));
    }
}
