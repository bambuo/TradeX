using TradeX.Core.Models;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class ConditionTreeEvaluatorTests
{
    private readonly ConditionTreeEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_LeafGreaterThan_ReturnsTrue()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = "RSI",
            Comparison = ">",
            Value = 30
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_LeafLessThan_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = "RSI",
            Comparison = "<",
            Value = 30
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_LeafGreaterThanOrEqual_BoundaryValue_ReturnsTrue()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = "SMA_20",
            Comparison = ">=",
            Value = 100
        };
        Dictionary<string, decimal> values = new()
        {
            ["SMA_20"] = 100
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_LeafLessThanOrEqual_BoundaryValue_ReturnsTrue()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = "SMA_20",
            Comparison = "<=",
            Value = 100
        };
        Dictionary<string, decimal> values = new()
        {
            ["SMA_20"] = 100
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_LeafEqualWithinTolerance_ReturnsTrue()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = "RSI",
            Comparison = "==",
            Value = 50.00005m
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 50
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_IndicatorNotFound_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = "RSI",
            Comparison = ">",
            Value = 30
        };
        Dictionary<string, decimal> values = [];

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_UnknownComparison_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = "RSI",
            Comparison = "INVALID",
            Value = 30
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_NullIndicator_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "",
            Indicator = null,
            Comparison = ">",
            Value = 30
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_AndOperator_AllTrue_ReturnsTrue()
    {
        var node = new ConditionNode
        {
            Operator = "AND",
            Conditions =
            [
                new() { Operator = "", Indicator = "RSI", Comparison = ">", Value = 30 },
                new() { Operator = "", Indicator = "SMA_20", Comparison = "<", Value = 200 }
            ]
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45, ["SMA_20"] = 150
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_AndOperator_OneFalse_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "AND",
            Conditions =
            [
                new() { Operator = "", Indicator = "RSI", Comparison = ">", Value = 30 },
                new() { Operator = "", Indicator = "SMA_20", Comparison = ">", Value = 200 }
            ]
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45, ["SMA_20"] = 150
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_AndOperator_EmptyConditions_ReturnsTrue()
    {
        // AND 空数组按"空真"语义返回 true, 与 JSON 解析层空条件不触发的语义在调用方组合: 引擎在空 JSON 时直接返回 false 不会走到这里
        var node = new ConditionNode
        {
            Operator = "AND",
            Conditions = []
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_OrOperator_OneTrue_ReturnsTrue()
    {
        var node = new ConditionNode
        {
            Operator = "OR",
            Conditions =
            [
                new() { Operator = "", Indicator = "RSI", Comparison = ">", Value = 50 },
                new() { Operator = "", Indicator = "SMA_20", Comparison = "<", Value = 200 }
            ]
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45, ["SMA_20"] = 150
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_OrOperator_AllFalse_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "OR",
            Conditions =
            [
                new() { Operator = "", Indicator = "RSI", Comparison = ">", Value = 50 },
                new() { Operator = "", Indicator = "SMA_20", Comparison = ">", Value = 200 }
            ]
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45, ["SMA_20"] = 150
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_OrOperator_EmptyConditions_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "OR",
            Conditions = []
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_NotOperator_InvertsTrue_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "NOT",
            Conditions =
            [
                new() { Operator = "", Indicator = "RSI", Comparison = ">", Value = 30 }
            ]
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_NotOperator_InvertsFalse_ReturnsTrue()
    {
        var node = new ConditionNode
        {
            Operator = "NOT",
            Conditions =
            [
                new() { Operator = "", Indicator = "RSI", Comparison = ">", Value = 50 }
            ]
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_NotOperator_ZeroConditions_ReturnsFalse()
    {
        var node = new ConditionNode
        {
            Operator = "NOT",
            Conditions = []
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_CrossAbove_ConstantThreshold_FiresOnCross()
    {
        // 常量阈值穿越：上一根 <= 阈值、当前 > 阈值
        var node = new ConditionNode { Operator = "", Indicator = "RSI", Comparison = "CrossAbove", Value = 50 };
        var result = _evaluator.Evaluate(node, new() { ["RSI"] = 55 }, new() { ["RSI"] = 48 });
        Assert.True(result);
    }

    [Fact]
    public void Evaluate_CrossAbove_RefIndicator_NoCrossWhenAlreadyAbove_ReturnsFalse()
    {
        // 指标对指标穿越：上一根 EMA(105) 已在 SMA(100) 之上，当前仍在之上 → 不应判为金叉。
        // 修复前的 bug 会用“当前 SMA”作为 prev 端基准，误判为穿越（105 <= 107 成立）。
        var node = new ConditionNode { Operator = "", Indicator = "EMA", Comparison = "CrossAbove", Ref = "SMA", Value = 1m };
        var current = new Dictionary<string, decimal> { ["EMA"] = 108, ["SMA"] = 107 };
        var previous = new Dictionary<string, decimal> { ["EMA"] = 105, ["SMA"] = 100 };

        var result = _evaluator.Evaluate(node, current, previous);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_CrossAbove_RefIndicator_RealCrossDetected_ReturnsTrue()
    {
        // 真正的金叉：上一根 EMA(100) 在 SMA(101) 之下，当前 EMA(103) 升破 SMA(99)。
        // 修复前会用“当前 SMA(99)”作为 prev 端基准，导致 100 <= 99 不成立而漏判。
        var node = new ConditionNode { Operator = "", Indicator = "EMA", Comparison = "CrossAbove", Ref = "SMA", Value = 1m };
        var current = new Dictionary<string, decimal> { ["EMA"] = 103, ["SMA"] = 99 };
        var previous = new Dictionary<string, decimal> { ["EMA"] = 100, ["SMA"] = 101 };

        var result = _evaluator.Evaluate(node, current, previous);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_CrossBelow_RefIndicator_RealCrossDetected_ReturnsTrue()
    {
        // 真正的死叉：上一根 EMA(101) 在 SMA(100) 之上，当前 EMA(98) 跌破 SMA(102)。
        var node = new ConditionNode { Operator = "", Indicator = "EMA", Comparison = "CrossBelow", Ref = "SMA", Value = 1m };
        var current = new Dictionary<string, decimal> { ["EMA"] = 98, ["SMA"] = 102 };
        var previous = new Dictionary<string, decimal> { ["EMA"] = 101, ["SMA"] = 100 };

        var result = _evaluator.Evaluate(node, current, previous);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_NestedAndOr_ComplexTree_ReturnsCorrectResult()
    {
        var node = new ConditionNode
        {
            Operator = "AND",
            Conditions =
            [
                new()
                {
                    Operator = "OR",
                    Conditions =
                    [
                        new() { Operator = "", Indicator = "RSI", Comparison = ">", Value = 30 },
                        new() { Operator = "", Indicator = "RSI", Comparison = "<", Value = 20 }
                    ]
                },
                new() { Operator = "", Indicator = "SMA_20", Comparison = ">", Value = 100 }
            ]
        };
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45, ["SMA_20"] = 150
        };

        var result = _evaluator.Evaluate(node, values, []);

        Assert.True(result);
    }
}

public class ConditionEvaluatorTests
{
    private readonly ConditionTreeEvaluator _treeEvaluator = new();
    private readonly ConditionEvaluator _evaluator;

    public ConditionEvaluatorTests()
    {
        _evaluator = new ConditionEvaluator(_treeEvaluator);
    }

    [Fact]
    public void Evaluate_EmptyJson_ReturnsFalse()
    {
        // 空条件视为不触发, 避免每根 K 线开/平仓
        var result = _evaluator.Evaluate("{}", [], []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_NullJson_ReturnsFalse()
    {
        var result = _evaluator.Evaluate("", [], []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_WhitespaceJson_ReturnsFalse()
    {
        var result = _evaluator.Evaluate("   ", [], []);

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_ValidJson_ReturnsEvaluatedResult()
    {
        var json = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""";
        Dictionary<string, decimal> values = new()
        {
            ["RSI"] = 45
        };

        var result = _evaluator.Evaluate(json, values, []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_InvalidJson_ReturnsFalse()
    {
        // 损坏的策略 JSON 不应让整轮回测崩溃, 按"不触发"兜底
        var result = _evaluator.Evaluate("not json", [], []);

        Assert.False(result);
    }
}
