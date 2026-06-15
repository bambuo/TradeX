using System.Text.Json;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Models;
using TradeX.Core.Rules;
using TradeX.Trading.Rules;
using TradeX.Trading.Rules.Nodes;

namespace TradeX.Tests.Rules;

public class ChainNodeTests
{
    private static ChainState CreateState(
        Dictionary<string, decimal>? signals = null,
        decimal currentPrice = 50000m)
    {
        var sigs = new Dictionary<string, Signal>();
        if (signals is not null)
            foreach (var (k, v) in signals)
                sigs[k] = new Signal { Name = k, Value = v };
        return new ChainState
        {
            Signals = sigs,
            Context = new EvalContext
            {
                Pair = "BTC/USDT", CurrentPrice = currentPrice,
                Portfolio = new PortfolioSnapshot { TotalEquity = 10000, AvailableCash = 5000 },
                Position = null, ScopeKey = "test|BTC/USDT",
            },
        };
    }

    private static JsonElement ToJsonElement(string json)
        => JsonSerializer.Deserialize<JsonElement>(json);

    // ═══════════════════════════════════════════════════════════════
    // Gate 节点测试
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RegimeGate_ShouldBlock_WhenRegimeNotAllowed()
    {
        var state = CreateState(new() { ["MARKET_REGIME"] = 0 });
        var node = new RegimeGateNode(ToJsonElement("""{"AllowedRegimes":["1"]}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task RegimeGate_ShouldPass_WhenRegimeAllowed()
    {
        var state = CreateState(new() { ["MARKET_REGIME"] = 1 });
        var node = new RegimeGateNode(ToJsonElement("""{"AllowedRegimes":["1"]}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.False(state.Blocked);
    }

    [Fact]
    public async Task TimeGate_WindowMode_ShouldBlock_OutsideWindow()
    {
        var state = CreateState();
        state.Context.EvaluationTime = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var node = new TimeGateNode(ToJsonElement("""{"Mode":"WINDOW","Windows":[{"DayOfWeek":0,"StartTime":"08:00","EndTime":"10:00"}]}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task TimeGate_IntervalMode_ShouldBlock_AtOddHour()
    {
        var state = CreateState();
        state.Context.EvaluationTime = new DateTime(2026, 6, 15, 1, 30, 0, DateTimeKind.Utc);
        var node = new TimeGateNode(ToJsonElement("""{"Mode":"INTERVAL","IntervalMinutes":120}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task SignalGate_ShouldBlock_WhenSignalBelowThreshold()
    {
        var state = CreateState(new() { ["RSI_14"] = 35 });
        var node = new SignalGateNode(ToJsonElement("""{"Signal":"RSI_14","Op":"<=","Threshold":30}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task CapitalGate_ShouldBlock_WhenInsufficientCash()
    {
        var state = CreateState();
        var node = new CapitalGateNode(ToJsonElement("""{"MinAvailableCash":10000}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task PositionGate_ShouldPass_WhenOpenAndRequireOpen()
    {
        var state = CreateState();
        state.Context.Position = new PositionSnapshot { Quantity = 1, EntryPrice = 45000 };
        var node = new PositionGateNode(ToJsonElement("""{"Require":"OPEN"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.False(state.Blocked);
    }

    [Fact]
    public async Task PositionGate_ShouldBlock_WhenClosedAndRequireOpen()
    {
        var state = CreateState();
        var node = new PositionGateNode(ToJsonElement("""{"Require":"OPEN"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task PositionGate_ShouldBlock_WhenOpenAndRequireClosed()
    {
        var state = CreateState();
        state.Context.Position = new PositionSnapshot { Quantity = 1, EntryPrice = 45000 };
        var node = new PositionGateNode(ToJsonElement("""{"Require":"CLOSED"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task PairGate_ShouldPass_WhenPairInWhitelist()
    {
        var state = CreateState();
        state.Context.Pair = "BTC/USDT";
        var node = new PairGateNode(ToJsonElement("""{"Whitelist":["BTC/USDT","ETH/USDT"]}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.False(state.Blocked);
    }

    [Fact]
    public async Task PairGate_ShouldBlock_WhenPairNotInWhitelist()
    {
        var state = CreateState();
        state.Context.Pair = "BTC/USDT";
        var node = new PairGateNode(ToJsonElement("""{"Whitelist":["ETH/USDT","SOL/USDT"]}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    // ═══════════════════════════════════════════════════════════════
    // Filter 节点测试
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MinNotional_ShouldFilterSmallBuy()
    {
        var state = CreateState(currentPrice: 50000m);
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.002m, Pair = "BTC/USDT" });
        var node = new MinNotionalNode(ToJsonElement("""{"MinNotional":500}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Empty(state.Actions);
    }

    [Fact]
    public async Task MaxSlippage_ShouldFilter_WhenSlippageHigh()
    {
        var state = CreateState(new() { ["ESTIMATED_SLIPPAGE"] = 5m });
        var node = new MaxSlippageNode(ToJsonElement("""{"MaxSlippagePercent":2}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    [Fact]
    public async Task LiquidityFilter_ShouldFilter_WhenDepthInsufficient()
    {
        var state = CreateState(new() { ["ASK_AGGREGATE_DEPTH"] = 1m }, currentPrice: 50000m);
        var node = new LiquidityFilterNode(ToJsonElement("""{"Side":"SELL","MinDepth":100000}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.True(state.Blocked);
    }

    // ═══════════════════════════════════════════════════════════════
    // Derive 节点测试
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CrossoverCheck_ShouldDetectGoldenCross()
    {
        var sigs = new Dictionary<string, Signal>
        {
            ["MA_FAST"] = new() { Name = "MA_FAST", Value = 11, PrevValue = 9 },
            ["MA_SLOW"] = new() { Name = "MA_SLOW", Value = 10, PrevValue = 10 },
        };
        var state = new ChainState { Signals = sigs, Context = new EvalContext { Pair = "BTC/USDT", CurrentPrice = 50000m, Portfolio = new PortfolioSnapshot { TotalEquity = 10000 }, ScopeKey = "test|BTC/USDT" } };
        var node = new CrossoverCheckNode(ToJsonElement("""{"FastSignal":"MA_FAST","SlowSignal":"MA_SLOW","OutputKey":"cross"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(1m, state.DerivedValues["cross"]);
    }

    [Fact]
    public async Task CrossoverCheck_ShouldDetectDeadCross()
    {
        var sigs = new Dictionary<string, Signal>
        {
            ["MA_FAST"] = new() { Name = "MA_FAST", Value = 9, PrevValue = 10 },
            ["MA_SLOW"] = new() { Name = "MA_SLOW", Value = 10, PrevValue = 10 },
        };
        var state = new ChainState { Signals = sigs, Context = new EvalContext { Pair = "BTC/USDT", CurrentPrice = 50000m, Portfolio = new PortfolioSnapshot { TotalEquity = 10000 }, ScopeKey = "test|BTC/USDT" } };
        var node = new CrossoverCheckNode(ToJsonElement("""{"FastSignal":"MA_FAST","SlowSignal":"MA_SLOW","OutputKey":"cross"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(-1m, state.DerivedValues["cross"]);
    }

    [Fact]
    public async Task AtrStopCalc_ShouldComputeStopLossAndTakeProfit()
    {
        var state = CreateState(new() { ["ATR_14"] = 2000m }, currentPrice: 50000m);
        var node = new AtrStopCalcNode(ToJsonElement("""{"AtrSignal":"ATR_14","Multiplier":2,"LongStopKey":"ls","LongTpKey":"ltp","ShortStopKey":"ss","ShortTpKey":"stp"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(46000m, state.DerivedValues["ls"]);
        Assert.Equal(54000m, state.DerivedValues["ltp"]);
    }

    [Fact]
    public async Task VolatilityScaling_ShouldScaleDown_WhenVolHigh()
    {
        var state = CreateState(new() { ["VOLATILITY_24H"] = 5m });
        var node = new VolatilityScalingNode(ToJsonElement("""{"Signal":"VOLATILITY_24H","BaseValue":1,"OutputKey":"vol_scaling"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(0.2m, state.DerivedValues["vol_scaling"]);
    }

    [Fact]
    public async Task TrailingStopCalc_ShouldComputeTrailingPrice()
    {
        var state = CreateState(currentPrice: 50000m);
        state.Context.Position = new PositionSnapshot { Quantity = 1, EntryPrice = 45000 };
        var node = new TrailingStopCalcNode(ToJsonElement("""{"TrailPercent":5,"OutputKey":"trailing_stop_price"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(47500m, state.DerivedValues["trailing_stop_price"]);
    }

    [Fact]
    public async Task GridPriceLevel_ShouldEmitLinearLevels()
    {
        var state = CreateState();
        var node = new GridPriceLevelNode(ToJsonElement("""{"TopPrice":60000,"BottomPrice":40000,"GridCount":3,"Mode":"LINEAR","OutputKey":"grid"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(3m, state.DerivedValues["grid_COUNT"]);
        Assert.Equal(40000m, state.DerivedValues["grid_0"]);
        Assert.Equal(50000m, state.DerivedValues["grid_1"]);
        Assert.Equal(60000m, state.DerivedValues["grid_2"]);
    }

    [Fact]
    public async Task GridPriceLevel_ShouldEmitGeometricLevels()
    {
        var state = CreateState();
        var node = new GridPriceLevelNode(ToJsonElement("""{"TopPrice":80000,"BottomPrice":20000,"GridCount":3,"Mode":"GEOMETRIC","OutputKey":"grid"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(3m, state.DerivedValues["grid_COUNT"]);
        Assert.Equal(20000m, state.DerivedValues["grid_0"]);
        Assert.Equal(40000m, state.DerivedValues["grid_1"]);
        Assert.Equal(80000m, state.DerivedValues["grid_2"]);
    }

    [Fact]
    public async Task CorrelationScore_ShouldReturnPlusOne_WhenDirectionSame()
    {
        var sigs = new Dictionary<string, Signal>
        {
            ["SIG_A"] = new() { Name = "SIG_A", Value = 12, PrevValue = 10 },
            ["SIG_B"] = new() { Name = "SIG_B", Value = 8, PrevValue = 6 },
        };
        var state = new ChainState { Signals = sigs, Context = new EvalContext { Pair = "BTC/USDT", CurrentPrice = 50000m, Portfolio = new PortfolioSnapshot { TotalEquity = 10000 }, ScopeKey = "test|BTC/USDT" } };
        var node = new CorrelationScoreNode(ToJsonElement("""{"SignalA":"SIG_A","SignalB":"SIG_B","OutputKey":"corr"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(1m, state.DerivedValues["corr"]);
    }

    [Fact]
    public async Task CorrelationScore_ShouldReturnMinusOne_WhenDirectionOpposite()
    {
        var sigs = new Dictionary<string, Signal>
        {
            ["SIG_A"] = new() { Name = "SIG_A", Value = 12, PrevValue = 10 },
            ["SIG_B"] = new() { Name = "SIG_B", Value = 5, PrevValue = 7 },
        };
        var state = new ChainState { Signals = sigs, Context = new EvalContext { Pair = "BTC/USDT", CurrentPrice = 50000m, Portfolio = new PortfolioSnapshot { TotalEquity = 10000 }, ScopeKey = "test|BTC/USDT" } };
        var node = new CorrelationScoreNode(ToJsonElement("""{"SignalA":"SIG_A","SignalB":"SIG_B","OutputKey":"corr"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Equal(-1m, state.DerivedValues["corr"]);
    }

    // ═══════════════════════════════════════════════════════════════
    // Size 节点测试
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FixedSize_ShouldEmitSizeDecision()
    {
        var state = CreateState();
        var node = new FixedSizeNode(ToJsonElement("""{"Amount":500,"Currency":"USDT"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.SizeDecisions);
        Assert.Equal(500m, state.SizeDecisions[0].Amount);
    }

    [Fact]
    public async Task AccountRatioSize_ShouldCalculateBasedOnEquity()
    {
        var state = CreateState();
        var node = new AccountRatioSizeNode(ToJsonElement("""{"Ratio":0.05}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.SizeDecisions);
        Assert.Equal(500m, state.SizeDecisions[0].Amount);
    }

    [Fact]
    public async Task PyramidingSize_ShouldIncreaseWithLevel()
    {
        var state = CreateState();
        state.Context.Position = new PositionSnapshot { LotCount = 1 };
        var node = new PyramidingSizeNode(ToJsonElement("""{"BaseAmount":100,"Multiplier":2,"MaxLevel":5}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.SizeDecisions);
        Assert.Equal(200m, state.SizeDecisions[0].Amount);
    }

    [Fact]
    public async Task PortfolioAllocSize_ShouldCalculateAllocation()
    {
        var state = CreateState();
        var node = new PortfolioAllocSizeNode(ToJsonElement("""{"AllocationPercent":20}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.SizeDecisions);
        Assert.Equal(2000m, state.SizeDecisions[0].Amount);
    }

    // ═══════════════════════════════════════════════════════════════
    // Action 节点测试
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SignalAction_ShouldEmitBuy_WhenSignalAboveThreshold()
    {
        var state = CreateState(new() { ["RSI_14"] = 80m });
        state.SizeDecisions.Add(new SizeDecision { Intent = "ENTER", Amount = 1000 });
        var node = new SignalActionNode(ToJsonElement("""{"BuySignal":"RSI_14","SellSignal":"","Threshold":50,"Direction":"ABOVE"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.Equal("BUY", state.Actions[0].Intent);
    }

    [Fact]
    public async Task TrailingStop_ShouldEmitSell_WhenPriceBelowStop()
    {
        var state = CreateState(currentPrice: 50000m);
        state.Context.Position = new PositionSnapshot { Quantity = 1, EntryPrice = 45000 };
        var node = new TrailingStopActionNode(ToJsonElement("""{"TrailPercent":0}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.Equal("SELL", state.Actions[0].Intent);
    }

    [Fact]
    public async Task TakeProfit_ShouldEmitSell_WhenPriceAboveTP()
    {
        var state = CreateState(currentPrice: 50000m);
        state.Context.Position = new PositionSnapshot { Quantity = 1, EntryPrice = 45000 };
        var node = new TakeProfitActionNode(ToJsonElement("""{"TpPercent":5}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.Equal("SELL", state.Actions[0].Intent);
    }

    [Fact]
    public async Task Martingale_ShouldIncreaseSize_WhenLevelIncreases()
    {
        var state = CreateState(currentPrice: 50000m);
        var store = Substitute.For<IStateNodeStore>();
        store.ReadStateAsync("test|BTC/USDT", "martingale_action", Arg.Any<CancellationToken>())
             .Returns(new NodeState { Data = new() { ["step"] = JsonSerializer.SerializeToElement(2) } });
        state.Context.StateStore = store;
        var node = new MartingaleActionNode(ToJsonElement("""{"BaseAmount":100,"Multiplier":2,"MaxSteps":10}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.Equal("BUY", state.Actions[0].Intent);
        Assert.Equal(0.008m, state.Actions[0].Quantity);
    }

    // ═══════════════════════════════════════════════════════════════
    // Risk 节点测试
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MaxPositionSize_ShouldScaleExcessiveBuy()
    {
        var state = CreateState(currentPrice: 50000m);
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.3m, Pair = "BTC/USDT" });
        var node = new MaxPositionSizeNode(ToJsonElement("""{"MaxNotional":10000}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.Equal(0.2m, state.Actions[0].Quantity);
    }

    [Fact]
    public async Task MaxDrawdown_ShouldClearActions_WhenDrawdownExceeded()
    {
        var state = CreateState();
        state.Context.Portfolio!.Drawdown = 20m;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new MaxDrawdownNode(ToJsonElement("""{"MaxDrawdownPercent":15}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Empty(state.Actions);
        Assert.True(state.Terminated);
    }

    [Fact]
    public async Task ConsecutiveLossStop_ShouldClearActions_WhenLossesExceeded()
    {
        var state = CreateState();
        var store = Substitute.For<IStateNodeStore>();
        store.ReadStateAsync("test|BTC/USDT", "consecutive_loss_stop", Arg.Any<CancellationToken>())
             .Returns(new NodeState { Data = new() { ["consecutiveLosses"] = JsonSerializer.SerializeToElement(5) } });
        state.Context.StateStore = store;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new ConsecutiveLossStopNode(ToJsonElement("""{"MaxConsecutiveLosses":3}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Empty(state.Actions);
        Assert.True(state.Terminated);
    }

    [Fact]
    public async Task QualityFilter_ShouldScaleDown_WhenQualityLow()
    {
        var state = CreateState(new() { ["SIGNAL_QUALITY"] = 0.3m });
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m, Pair = "BTC/USDT" });
        var node = new QualityFilterNode(ToJsonElement("""{"DegradeMap":{"SIGNAL_QUALITY":1}}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.Equal(0.03m, state.Actions[0].Quantity);
    }

    [Fact]
    public async Task Cooldown_ShouldRejectActions_WhenInCooldownPeriod()
    {
        var state = CreateState();
        var store = Substitute.For<IStateNodeStore>();
        store.ReadStateAsync("test|BTC/USDT", "cooldown", Arg.Any<CancellationToken>())
             .Returns(new NodeState { Data = new() { ["lastTradeAt"] = JsonSerializer.SerializeToElement(state.Context.EvaluationTime.AddMinutes(-5)) } });
        state.Context.StateStore = store;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new CooldownNode(ToJsonElement("""{"CooldownMinutes":30}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Empty(state.Actions);
    }

    [Fact]
    public async Task Cooldown_ShouldAllowActions_WhenCooldownExpired()
    {
        var state = CreateState();
        var store = Substitute.For<IStateNodeStore>();
        store.ReadStateAsync("test|BTC/USDT", "cooldown", Arg.Any<CancellationToken>())
             .Returns(new NodeState { Data = new() { ["lastTradeAt"] = JsonSerializer.SerializeToElement(state.Context.EvaluationTime.AddMinutes(-60)) } });
        state.Context.StateStore = store;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new CooldownNode(ToJsonElement("""{"CooldownMinutes":30}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
    }

    [Fact]
    public async Task DailyLossLimit_ShouldClearActions_WhenDailyLossExceeded()
    {
        var state = CreateState();
        state.Context.Portfolio!.DailyPnl = -2000m;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new DailyLossLimitNode(ToJsonElement("""{"MaxDailyLoss":1500}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Empty(state.Actions);
        Assert.True(state.Terminated);
    }

    [Fact]
    public async Task DailyLossLimit_ShouldAllowActions_WhenDailyLossBelowLimit()
    {
        var state = CreateState();
        state.Context.Portfolio!.DailyPnl = -500m;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new DailyLossLimitNode(ToJsonElement("""{"MaxDailyLoss":1500}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.False(state.Terminated);
    }

    // ═══════════════════════════════════════════════════════════════
    // Override 节点测试
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Override_KillSwitch_ShouldClearActions()
    {
        var state = CreateState();
        state.Context.IsKillSwitchActive = sk => true;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new KillSwitchNode(ToJsonElement("""{"Key":"test"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Empty(state.Actions);
        Assert.True(state.Terminated);
    }

    [Fact]
    public async Task Override_KillSwitch_ShouldPass_WhenNotActive()
    {
        var state = CreateState();
        state.Context.IsKillSwitchActive = sk => false;
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new KillSwitchNode(ToJsonElement("""{"Key":"test"}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Single(state.Actions);
        Assert.False(state.Terminated);
    }

    [Fact]
    public async Task Override_EmergencyExit_ShouldEmitSellAll()
    {
        var state = CreateState(new() { ["TRIGGER"] = 5m });
        state.Context.Position = new PositionSnapshot { Quantity = 1, EntryPrice = 45000 };
        state.Actions.Add(new ActionDecision { Intent = "BUY", Quantity = 0.1m });
        var node = new EmergencyExitNode(ToJsonElement("""{"Signal":"TRIGGER","Threshold":1,"Op":">="}"""));
        await node.ProcessAsync(state, CancellationToken.None);
        Assert.Contains(state.Actions, a => a.Intent == "SELL_ALL");
        Assert.True(state.Terminated);
    }
}
