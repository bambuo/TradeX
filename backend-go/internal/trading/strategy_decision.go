package trading

import (
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

func sdHold(reason string) domain.StrategyDecision {
	return domain.StrategyDecision{Action: domain.ActionHold, Reason: reason}
}

func sdEnter(reason string) domain.StrategyDecision {
	return domain.StrategyDecision{Action: domain.ActionEnter, Reason: reason}
}

func sdExitAll(reason string) domain.StrategyDecision {
	return domain.StrategyDecision{Action: domain.ActionExit, Reason: reason}
}

func sdAddGrid(quoteSize decimal.Decimal, reason string) domain.StrategyDecision {
	return domain.StrategyDecision{Action: domain.ActionAddGrid, QuoteSize: quoteSize, Reason: reason}
}

func sdReduceGrid(reason string) domain.StrategyDecision {
	return domain.StrategyDecision{Action: domain.ActionReduceGrid, Reason: reason}
}

type StrategyDecisionEngine struct {
	condEval *ConditionEvaluator
}

func NewStrategyDecisionEngine(condEval *ConditionEvaluator) *StrategyDecisionEngine {
	return &StrategyDecisionEngine{condEval: condEval}
}

func (e *StrategyDecisionEngine) Decide(input domain.DecisionInput) domain.StrategyDecision {
	gridRule := domain.ParseExecutionRule(input.ExecutionRule)
	if gridRule != nil {
		state := VolatilityGridState{
			AverageEntryPrice: input.AverageEntryPrice,
			QuantityHeld:      input.QuantityHeld,
			PyramidingLevel:   input.LotCount,
		}
		executor := NewVolatilityGridExecutor(defaultGridRuleFromDomain(gridRule))
		decision := executor.Decide(state, input.CurrentPrice)

		switch decision.Action {
		case GridBuy:
			return sdAddGrid(defaultGridRuleFromDomain(gridRule).BasePositionSize, decision.Reason)
		case GridSell:
			return sdReduceGrid(decision.Reason)
		default:
			return sdHold(decision.Reason)
		}
	}
	return e.decideCondition(input)
}

func (e *StrategyDecisionEngine) decideCondition(input domain.DecisionInput) domain.StrategyDecision {
	hasPosition := input.QuantityHeld.IsPositive()

	if !hasPosition {
		if domain.HasCondition(input.EntryCondition) &&
			e.condEval.Evaluate(input.EntryCondition, input.CurrentValues, input.PreviousValues) {
			return sdEnter("条件入场")
		}
		return sdHold("无入场信号")
	}

	if domain.HasCondition(input.ExitCondition) &&
		e.condEval.Evaluate(input.ExitCondition, input.CurrentValues, input.PreviousValues) {
		return sdExitAll("条件出场")
	}

	return sdHold("持有中，无出场信号")
}

func defaultGridRuleFromDomain(r *domain.RawGridRule) VolatilityGridExecutionRule {
	rule := DefaultVolatilityGridRule()
	if r != nil {
		rule.RebalancePercent = decimal.NewFromFloat(r.RebalancePercent)
		rule.BasePositionSize = r.BasePositionSize
		rule.MaxPyramidingLevels = r.MaxPyramidingLevels
	}
	return rule
}
