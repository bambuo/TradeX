using TradeX.Trading.Engine;

namespace TradeX.Tests.Trading;

public class VolatilityGridExecutorTests
{
    private static VolatilityGridExecutionRule Rule(decimal rebalance = 2m, decimal basePos = 100m,
        decimal maxPos = 500m, int maxLevels = 5) =>
        new("volatility_grid", 1m, rebalance, basePos, maxPos, maxLevels, true, 0.0005m, 200m);

    [Fact]
    public void Decide_NoPosition_OpensFirstEntry()
    {
        var exec = new VolatilityGridExecutor(Rule());
        var d = exec.Decide(VolatilityGridState.Empty, currentPrice: 50000m);

        Assert.Equal(VolatilityGridAction.Buy, d.Action);
        Assert.Equal(1, d.NewLevel);
        Assert.Equal(100m / 50000m, d.Quantity);
    }

    [Fact]
    public void Decide_PriceDropsByRebalance_AddsPosition()
    {
        var exec = new VolatilityGridExecutor(Rule(rebalance: 2m));
        var state = new VolatilityGridState(AverageEntryPrice: 50000m, QuantityHeld: 0.002m, PyramidingLevel: 1);

        var d = exec.Decide(state, currentPrice: 49000m);  // -2%

        Assert.Equal(VolatilityGridAction.Buy, d.Action);
        Assert.Equal(2, d.NewLevel);
    }

    [Fact]
    public void Decide_PriceRisesByRebalance_ReducesPosition()
    {
        var exec = new VolatilityGridExecutor(Rule(rebalance: 2m));
        var state = new VolatilityGridState(50000m, 0.002m, 1);

        var d = exec.Decide(state, currentPrice: 51000m);  // +2%

        Assert.Equal(VolatilityGridAction.Sell, d.Action);
        Assert.Equal(0, d.NewLevel);
    }

    [Fact]
    public void Decide_DeviationBelowThreshold_Holds()
    {
        var exec = new VolatilityGridExecutor(Rule(rebalance: 2m));
        var state = new VolatilityGridState(50000m, 0.002m, 1);

        var d = exec.Decide(state, currentPrice: 50500m);  // +1%

        Assert.Equal(VolatilityGridAction.Hold, d.Action);
    }

    [Fact]
    public void Decide_PyramidingMaxReached_HoldsOnDrop()
    {
        var exec = new VolatilityGridExecutor(Rule(rebalance: 2m, maxLevels: 3));
        var state = new VolatilityGridState(50000m, 0.006m, 3);

        var d = exec.Decide(state, currentPrice: 48000m);  // -4%

        Assert.Equal(VolatilityGridAction.Hold, d.Action);
        Assert.Contains("加仓上限", d.Reason);
    }

    [Fact]
    public void Decide_MaxPositionSizeReached_HoldsOnDrop()
    {
        // base=100, maxPos=250 → 加 2 次后下次会突破
        var exec = new VolatilityGridExecutor(Rule(rebalance: 2m, basePos: 100m, maxPos: 250m, maxLevels: 10));
        // 已有 200 名义价值 (2 次), 再加 100 会超
        var state = new VolatilityGridState(50000m, 0.004m, 2);

        var d = exec.Decide(state, currentPrice: 48000m);

        Assert.Equal(VolatilityGridAction.Hold, d.Action);
        Assert.Contains("超过上限", d.Reason);
    }

    [Fact]
    public void ApplyBuy_UpdatesAverageAndLevel()
    {
        var s = new VolatilityGridState(50000m, 0.002m, 1);
        var next = s.ApplyBuy(price: 48000m, qty: 0.002m, newLevel: 2);

        Assert.Equal(2, next.PyramidingLevel);
        Assert.Equal(0.004m, next.QuantityHeld);
        Assert.Equal((50000m * 0.002m + 48000m * 0.002m) / 0.004m, next.AverageEntryPrice);
    }

    [Fact]
    public void ApplySell_ReducesQuantityKeepsAvg()
    {
        var s = new VolatilityGridState(50000m, 0.004m, 2);
        var next = s.ApplySell(qty: 0.002m, newLevel: 1);

        Assert.Equal(1, next.PyramidingLevel);
        Assert.Equal(0.002m, next.QuantityHeld);
        Assert.Equal(50000m, next.AverageEntryPrice);
    }

    [Fact]
    public void ApplySell_AllQuantity_ResetsAvg()
    {
        var s = new VolatilityGridState(50000m, 0.002m, 1);
        var next = s.ApplySell(qty: 0.002m, newLevel: 0);

        Assert.Equal(0m, next.QuantityHeld);
        Assert.Equal(0m, next.AverageEntryPrice);
    }

    [Fact]
    public void Decide_FullCycle_BuyDropBuyRiseSell_ProducesConsistentLevels()
    {
        var exec = new VolatilityGridExecutor(Rule(rebalance: 2m));
        var s = VolatilityGridState.Empty;

        // 1. 50000 开仓
        var d1 = exec.Decide(s, 50000m);
        s = s.ApplyBuy(50000m, d1.Quantity, d1.NewLevel);
        Assert.Equal(1, s.PyramidingLevel);

        // 2. 跌 2% 加仓
        var d2 = exec.Decide(s, 49000m);
        Assert.Equal(VolatilityGridAction.Buy, d2.Action);
        s = s.ApplyBuy(49000m, d2.Quantity, d2.NewLevel);
        Assert.Equal(2, s.PyramidingLevel);

        // 3. 涨回均价以上 2% 减仓
        var avg = s.AverageEntryPrice;
        var d3 = exec.Decide(s, avg * 1.02m + 1m);
        Assert.Equal(VolatilityGridAction.Sell, d3.Action);
        s = s.ApplySell(d3.Quantity, d3.NewLevel);
        Assert.Equal(1, s.PyramidingLevel);
    }
}
