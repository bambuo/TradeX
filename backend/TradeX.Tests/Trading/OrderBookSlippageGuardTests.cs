using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Trading.Risk;

namespace TradeX.Tests.Trading;

public class OrderBookSlippageGuardTests
{
    private static OrderBook BuildBook(decimal[][] bids, decimal[][] asks)
    {
        var bidArr = new decimal[bids.Length, 2];
        for (var i = 0; i < bids.Length; i++) { bidArr[i, 0] = bids[i][0]; bidArr[i, 1] = bids[i][1]; }
        var askArr = new decimal[asks.Length, 2];
        for (var i = 0; i < asks.Length; i++) { askArr[i, 0] = asks[i][0]; askArr[i, 1] = asks[i][1]; }
        return new OrderBook(bidArr, askArr, DateTime.UtcNow);
    }

    [Fact]
    public void Estimate_BuyWithSingleLevelEnoughDepth_NoSlippage()
    {
        var book = BuildBook(
            bids: [[99m, 100m]],
            asks: [[100m, 100m]]);

        var r = new OrderBookSlippageGuard().Estimate(book, OrderSide.Buy, 10m, 100m);

        Assert.True(r.Sufficient);
        Assert.Equal(100m, r.AverageFillPrice);
        Assert.Equal(0m, r.SlippagePercent);
    }

    [Fact]
    public void Estimate_BuyEatsMultipleLevels_ReportsAverageAndSlippage()
    {
        var book = BuildBook(
            bids: [[99m, 50m]],
            asks: [[100m, 5m], [101m, 5m], [102m, 5m]]);

        // 买 15: 5@100 + 5@101 + 5@102 → avg = (500+505+510)/15 = 100.9999...
        var r = new OrderBookSlippageGuard().Estimate(book, OrderSide.Buy, 15m, 100m);

        Assert.True(r.Sufficient);
        Assert.Equal((500m + 505m + 510m) / 15m, r.AverageFillPrice);
        Assert.True(r.SlippagePercent > 0);
    }

    [Fact]
    public void Estimate_SellEatsMultipleLevels_SlippagePercentIsPositiveUnfavorable()
    {
        var book = BuildBook(
            bids: [[100m, 5m], [99m, 5m], [98m, 5m]],
            asks: [[101m, 50m]]);

        // 卖 15: 5@100 + 5@99 + 5@98 → avg < ref(100), 不利偏离为正
        var r = new OrderBookSlippageGuard().Estimate(book, OrderSide.Sell, 15m, 100m);

        Assert.True(r.Sufficient);
        Assert.True(r.SlippagePercent > 0);
    }

    [Fact]
    public void Estimate_InsufficientDepth_ReturnsFailure()
    {
        var book = BuildBook(
            bids: [[99m, 100m]],
            asks: [[100m, 1m]]);

        var r = new OrderBookSlippageGuard().Estimate(book, OrderSide.Buy, 10m, 100m);

        Assert.False(r.Sufficient);
        Assert.Contains("缺口", r.Reason);
    }

    [Fact]
    public void Estimate_EmptyBook_ReturnsFailure()
    {
        var book = BuildBook([], []);
        var r = new OrderBookSlippageGuard().Estimate(book, OrderSide.Buy, 10m, 100m);
        Assert.False(r.Sufficient);
    }

    [Fact]
    public void Estimate_ZeroQuantity_ReturnsFailure()
    {
        var book = BuildBook([], [[100m, 10m]]);
        var r = new OrderBookSlippageGuard().Estimate(book, OrderSide.Buy, 0m, 100m);
        Assert.False(r.Sufficient);
    }
}
