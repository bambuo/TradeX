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
    public void Evaluate_AndOperator_EmptyConditions_ReturnsFalse()
    {
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

        Assert.False(result);
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
    public void Evaluate_EmptyJson_ReturnsTrue()
    {
        var result = _evaluator.Evaluate("{}", [], []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_NullJson_ReturnsTrue()
    {
        var result = _evaluator.Evaluate("", [], []);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_WhitespaceJson_ReturnsTrue()
    {
        var result = _evaluator.Evaluate("   ", [], []);

        Assert.True(result);
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
    public void Evaluate_InvalidJson_ThrowsJsonException()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            _evaluator.Evaluate("not json", [], []));
    }
}
