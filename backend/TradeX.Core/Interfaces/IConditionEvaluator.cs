using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IConditionEvaluator
{
    bool Evaluate(string conditionJson, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues);
}

public interface IConditionTreeEvaluator
{
    bool Evaluate(ConditionNode node, Dictionary<string, decimal> indicatorValues, Dictionary<string, decimal> previousValues);
}
