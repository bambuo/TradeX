package engine

import (
	"encoding/json"

	"tradex/internal/domain"
)

func evaluateEntry(cond json.RawMessage, rule json.RawMessage, ctx EvaluationContext, posCtx domain.DecisionInput) domain.StrategyDecision {
	gridRule := domain.ParseExecutionRule(rule)
	if gridRule != nil {
		return domain.GridDecide(gridRule, posCtx)
	}

	if !domain.HasCondition(cond) {
		return domain.StrategyDecision{Action: domain.ActionHold, Reason: "no_entry_condition"}
	}

	entry, err := EvaluateCondition(cond, ctx)
	condResult := entry && err == nil
	if !condResult {
		return domain.StrategyDecision{
			Action:          domain.ActionHold,
			ConditionResult: &condResult,
		}
	}

	return domain.StrategyDecision{
		Action:          domain.ActionEnter,
		Reason:          "entry_condition_met",
		ConditionResult: &condResult,
	}
}

func evaluateExit(cond json.RawMessage, rule json.RawMessage, ctx EvaluationContext, posCtx domain.DecisionInput) domain.StrategyDecision {
	gridRule := domain.ParseExecutionRule(rule)
	if gridRule != nil {
		return domain.GridDecide(gridRule, posCtx)
	}

	if !domain.HasCondition(cond) {
		return domain.StrategyDecision{Action: domain.ActionHold, Reason: "no_exit_condition"}
	}

	exit, err := EvaluateCondition(cond, ctx)
	condResult := exit && err == nil
	if !condResult {
		return domain.StrategyDecision{
			Action:          domain.ActionHold,
			ConditionResult: &condResult,
		}
	}

	return domain.StrategyDecision{
		Action:          domain.ActionExit,
		Reason:          "exit_condition_met",
		ConditionResult: &condResult,
	}
}
