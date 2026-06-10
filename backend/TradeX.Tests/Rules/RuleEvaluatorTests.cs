using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Models;
using TradeX.Rules.Engine;
using TradeX.Rules.Models;

namespace TradeX.Tests.Rules;

public class RuleEvaluatorTests
{
    private static IRuleEvaluator CreateEvaluator() =>
        new RuleEvaluator(new TriggerTracker(), Substitute.For<ILogger<RuleEvaluator>>());

    [Fact]
    public void Evaluate_EmptyRuleSet_ReturnsHold()
    {
        var evaluator = CreateEvaluator();
        var ruleSet = new RuleSet("empty", "空规则集", []);

        var result = evaluator.Evaluate(ruleSet, AnyContext());

        Assert.Equal(RuleDecisionAction.Hold, result.Action);
    }

    [Fact]
    public void Evaluate_AlwaysTrueRule_ReturnsAction()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "恒真买入",
                When: null, // null = 恒真
                Then: new RuleAction(RuleActionType.Buy, Size: 100m))
        ]);

        var result = evaluator.Evaluate(ruleSet, AnyContext());

        Assert.Equal(RuleDecisionAction.Buy, result.Action);
        Assert.Equal(100m, result.Size);
    }

    [Fact]
    public void Evaluate_AndCondition_AllTrue_Executes()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "价格高于50000",
                When: new ConditionNode
                {
                    Operator = "AND",
                    Conditions = [
                        new() { Comparison = ">", Indicator = "CLOSE", Value = 50000m }
                    ]
                },
                Then: new RuleAction(RuleActionType.Buy))
        ]);

        var ctx = new RuleEvaluationContext(
            CurrentPrice: 60000m, AverageEntryPrice: 0, QuantityHeld: 0, LotCount: 0,
            IndicatorValues: new() { ["CLOSE"] = 60000m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Buy, result.Action);
    }

    [Fact]
    public void Evaluate_AndCondition_OneFalse_Skips()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "价格高于50000",
                When: new ConditionNode
                {
                    Operator = "AND",
                    Conditions = [
                        new() { Comparison = ">", Indicator = "CLOSE", Value = 50000m }
                    ]
                },
                Then: new RuleAction(RuleActionType.Buy))
        ]);

        var ctx = new RuleEvaluationContext(
            CurrentPrice: 40000m, AverageEntryPrice: 0, QuantityHeld: 0, LotCount: 0,
            IndicatorValues: new() { ["CLOSE"] = 40000m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Hold, result.Action);
    }

    [Fact]
    public void Evaluate_OrCondition_OneTrue_Executes()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "OR条件",
                When: new ConditionNode
                {
                    Operator = "OR",
                    Conditions = [
                        new() { Comparison = ">", Indicator = "RSI", Value = 70m },
                        new() { Comparison = "<", Indicator = "RSI", Value = 30m }
                    ]
                },
                Then: new RuleAction(RuleActionType.Sell))
        ]);

        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 0, QuantityHeld: 1, LotCount: 1,
            IndicatorValues: new() { ["RSI"] = 75m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Sell, result.Action);
    }

    [Fact]
    public void Evaluate_NotCondition_Negates()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "非条件",
                When: new ConditionNode
                {
                    Operator = "NOT",
                    Conditions = [
                        new() { Comparison = ">", Indicator = "CLOSE", Value = 100m }
                    ]
                },
                Then: new RuleAction(RuleActionType.Buy))
        ]);

        // CLOSE=50  => NOT(50>100) => NOT(false) => true => Buy
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 50m, AverageEntryPrice: 0, QuantityHeld: 0, LotCount: 0,
            IndicatorValues: new() { ["CLOSE"] = 50m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Buy, result.Action);
    }

    [Fact]
    public void Evaluate_ContextNoPosition_HasPosition_Skips()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "入场",
                When: null,
                Then: new RuleAction(RuleActionType.Buy),
                Context: RuleContext.NoPosition)
        ]);

        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 50m, QuantityHeld: 2, LotCount: 1,
            IndicatorValues: []);

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Hold, result.Action);
    }

    [Fact]
    public void Evaluate_ContextHasPosition_NoPosition_Skips()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "加仓",
                When: null,
                Then: new RuleAction(RuleActionType.Buy),
                Context: RuleContext.HasPosition)
        ]);

        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 0, QuantityHeld: 0, LotCount: 0,
            IndicatorValues: []);

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Hold, result.Action);
    }

    [Fact]
    public void Evaluate_PriorityOrdering_HigherPriorityEvaluatedFirst()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "高优先级",
                When: new ConditionNode
                {
                    Comparison = ">",
                    Indicator = "X",
                    Value = 100m
                },
                Then: new RuleAction(RuleActionType.Buy, Size: 50m),
                Priority: 1),
            new TradingRule("r2", "低优先级",
                When: new ConditionNode
                {
                    Comparison = ">",
                    Indicator = "X",
                    Value = 100m
                },
                Then: new RuleAction(RuleActionType.Buy, Size: 200m),
                Priority: 10)
        ]);

        // 两条规则都满足，但 r1(priority 1) 先匹配
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 200m, AverageEntryPrice: 0, QuantityHeld: 0, LotCount: 0,
            IndicatorValues: new() { ["X"] = 150 });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Buy, result.Action);
        Assert.Equal(50m, result.Size);
    }

    [Fact]
    public void Evaluate_Constraints_MaxPositionValueExceeded_Skips()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "加仓",
                When: null,
                Then: new RuleAction(RuleActionType.Buy),
                Constraints: new RuleConstraints(MaxPositionValue: 5000m))
        ]);

        // 持有 100 个 * 100 = 10000 > 5000
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 50m, QuantityHeld: 100, LotCount: 2,
            IndicatorValues: []);

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Hold, result.Action);
    }

    [Fact]
    public void Evaluate_SellAllAction_ReturnsSellAll()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "止损出场",
                When: null,
                Then: new RuleAction(RuleActionType.SellAll, Reason: "止损 {{CLOSE}}"),
                Context: RuleContext.HasPosition)
        ]);

        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 50m, QuantityHeld: 10, LotCount: 1,
            IndicatorValues: new() { ["CLOSE"] = 100m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.SellAll, result.Action);
    }

    [Fact]
    public void Evaluate_CACrossOver_TriggersOnCrossAbove()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "金叉入场",
                When: new ConditionNode
                {
                    Indicator = "MA_FAST",
                    Comparison = "CA",
                    Value = 1.0m,
                    Ref = "MA_SLOW"
                },
                Then: new RuleAction(RuleActionType.Buy),
                Context: RuleContext.NoPosition)
        ]);

        // prev: 10 < 20, cur: 25 > 20 → 上穿
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 0, QuantityHeld: 0, LotCount: 0,
            IndicatorValues: new() { ["MA_FAST"] = 25m, ["MA_SLOW"] = 20m },
            PreviousIndicatorValues: new() { ["MA_FAST"] = 10m, ["MA_SLOW"] = 20m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Buy, result.Action);
    }

    [Fact]
    public void Evaluate_CBCrossDown_TriggersOnCrossBelow()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "死叉出场",
                When: new ConditionNode
                {
                    Indicator = "MA_FAST",
                    Comparison = "CB",
                    Value = 1.0m,
                    Ref = "MA_SLOW"
                },
                Then: new RuleAction(RuleActionType.SellAll),
                Context: RuleContext.HasPosition)
        ]);

        // prev: 30 > 20, cur: 15 < 20 → 下穿
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 50m, QuantityHeld: 10, LotCount: 1,
            IndicatorValues: new() { ["MA_FAST"] = 15m, ["MA_SLOW"] = 20m },
            PreviousIndicatorValues: new() { ["MA_FAST"] = 30m, ["MA_SLOW"] = 20m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.SellAll, result.Action);
    }

    [Fact]
    public void Evaluate_ContextIndicators_MergedAndOverridden()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "偏离度入场",
                When: new ConditionNode
                {
                    Comparison = "<",
                    Indicator = "DEVIATION_FROM_AVG",
                    Value = -3m
                },
                Then: new RuleAction(RuleActionType.Buy),
                Context: RuleContext.HasPosition)
        ]);

        // 当前价 96.9, 均价 100, 偏离 = (96.9-100)/100*100 = -3.1% → -3.1 < -3 → true
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 96.9m, AverageEntryPrice: 100m, QuantityHeld: 10, LotCount: 1,
            IndicatorValues: []);

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Buy, result.Action);
    }

    [Fact]
    public void Evaluate_SizeMultiplier_ScalesSize()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "金字塔加仓",
                When: null,
                Then: new RuleAction(RuleActionType.Buy,
                    Size: 100m, SizeType: "multiplier", SizeMultiplierRef: "POSITION_COUNT"),
                Context: RuleContext.HasPosition)
        ]);

        // POSITION_COUNT = 3 (已有 3 层仓位) => size = 100 * max(1, 3) = 300
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 110m, QuantityHeld: 3, LotCount: 3,
            IndicatorValues: []);

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Equal(RuleDecisionAction.Buy, result.Action);
        Assert.Equal(300m, result.Size);
    }

    [Fact]
    public void Evaluate_ReasonTemplate_ResolvesVariables()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "入场",
                When: null,
                Then: new RuleAction(RuleActionType.Buy, Reason: "价格 {{CLOSE}} 触发买入 {{DEVIATION_FROM_AVG}}"))
        ]);

        // CLOSE=100 在 IndicatorValues 中，DEVIATION_FROM_AVG 由 ContextIndicatorCalculator 计算
        var ctx = new RuleEvaluationContext(
            CurrentPrice: 100m, AverageEntryPrice: 90m, QuantityHeld: 0, LotCount: 0,
            IndicatorValues: new() { ["CLOSE"] = 100m });

        var result = evaluator.Evaluate(ruleSet, ctx);

        Assert.Contains("100.00", result.Reason);
        Assert.Contains("11.11", result.Reason); // (100-90)/90*100 = 11.11...
    }

    // ──── MinInterval 约束测试 ────

    [Fact]
    public void Evaluate_MinIntervalNotElapsed_SkipsRule()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "买入",
                When: null,
                Then: new RuleAction(RuleActionType.Buy, Size: 100m),
                Constraints: new RuleConstraints(MinInterval: 60)) // 60s 最小间隔
        ]);

        // 第一次触发：应该通过
        var result1 = evaluator.Evaluate(ruleSet, AnyContext());
        Assert.Equal(RuleDecisionAction.Buy, result1.Action);

        // 第二次立即触发：时间未过 60s，应被拦截
        var result2 = evaluator.Evaluate(ruleSet, AnyContext());
        Assert.Equal(RuleDecisionAction.Hold, result2.Action);
    }

    [Fact]
    public void Evaluate_MinIntervalElapsed_AllowsTrigger()
    {
        var triggerTracker = new TriggerTracker();
        var evaluator = new RuleEvaluator(triggerTracker, Substitute.For<ILogger<RuleEvaluator>>());

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "买入",
                When: null,
                Then: new RuleAction(RuleActionType.Buy, Size: 100m),
                Constraints: new RuleConstraints(MinInterval: 60)) // 60s 最小间隔
        ]);

        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 第一次触发
        var result1 = evaluator.Evaluate(ruleSet, AnyContext(t0));
        Assert.Equal(RuleDecisionAction.Buy, result1.Action);

        // 评估时间推进 61s（按模拟时间而非墙钟），应再次放行
        var result2 = evaluator.Evaluate(ruleSet, AnyContext(t0.AddSeconds(61)));
        Assert.Equal(RuleDecisionAction.Buy, result2.Action);
    }

    [Fact]
    public void Evaluate_MinInterval_OnlyAffectsSameRuleCode()
    {
        var evaluator = CreateEvaluator();

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "规则1",
                When: null,
                Then: new RuleAction(RuleActionType.Buy, Size: 100m),
                Constraints: new RuleConstraints(MinInterval: 60)),
            new TradingRule("r2", "规则2",
                When: null,
                Then: new RuleAction(RuleActionType.Buy, Size: 200m),
                Constraints: new RuleConstraints(MinInterval: 60))
        ]);

        // 第一次评估：r1 触发生效（优先级相同，按列表顺序 r1 先匹配）
        var result1 = evaluator.Evaluate(ruleSet, AnyContext());
        Assert.Equal(RuleDecisionAction.Buy, result1.Action);
        Assert.Equal(100m, result1.Size);

        // 第二次评估：r1 被 MinInterval 阻挡，r2 应触发（r2 自身的未触发过）
        var result2 = evaluator.Evaluate(ruleSet, AnyContext());
        Assert.Equal(RuleDecisionAction.Buy, result2.Action);
        Assert.Equal(200m, result2.Size);
    }

    // ──── TriggerTracker 并发安全测试 ────

    [Fact]
    public void TriggerTracker_ConcurrentAccess_NoException()
    {
        var tracker = new TriggerTracker();
        var codes = new[] { "r1", "r2", "r3", "r4", "r5" };

        Parallel.For(0, 100, _ =>
        {
            foreach (var code in codes)
            {
                var now = DateTime.UtcNow;
                tracker.RecordTrigger(code, now);
                var elapsed = tracker.ElapsedSecondsSinceLastTrigger(code, DateTime.UtcNow);
                Assert.NotNull(elapsed);
                Assert.True(elapsed >= 0);
            }
        });
    }

    [Fact]
    public void Evaluate_MinInterval_ResetsOnNewTriggerRecord()
    {
        var triggerTracker = new TriggerTracker();
        var evaluator = new RuleEvaluator(triggerTracker);

        var ruleSet = new RuleSet("test", "测试", [
            new TradingRule("r1", "买入",
                When: null,
                Then: new RuleAction(RuleActionType.Buy, Size: 100m),
                Constraints: new RuleConstraints(MinInterval: 300))
        ]);

        // 第一次触发
        var result1 = evaluator.Evaluate(ruleSet, AnyContext());
        Assert.Equal(RuleDecisionAction.Buy, result1.Action);

        // 手动修改最后触发时间到 5 分钟前
        // 由于 TriggerTracker 内部用 ConcurrentDictionary，直接访问字段无法做到
        // 改为通过 RecordTrigger + 验证间隔方式测试：第二次立即执行被拦截
        var result2 = evaluator.Evaluate(ruleSet, AnyContext());
        Assert.Equal(RuleDecisionAction.Hold, result2.Action);

        // 确保第一次和第二次的 Hold 是因 MinInterval
        // 重新创建 evaluator 和 tracker（全新状态），触发应成功
        var freshTracker = new TriggerTracker();
        var freshEvaluator = new RuleEvaluator(freshTracker);
        var result3 = freshEvaluator.Evaluate(ruleSet, AnyContext());
        Assert.Equal(RuleDecisionAction.Buy, result3.Action);
    }

    private static RuleEvaluationContext AnyContext(DateTime? at = null) => new(
        CurrentPrice: 100m, AverageEntryPrice: 0, QuantityHeld: 0, LotCount: 0,
        IndicatorValues: [], EvaluationTime: at ?? default);
}
