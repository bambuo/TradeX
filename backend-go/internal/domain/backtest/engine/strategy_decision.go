package engine

import (
	"encoding/json"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

type DecisionType string

const (
	DecisionEnter DecisionType = "enter"
	DecisionExit  DecisionType = "exit"
	DecisionHold  DecisionType = "hold"
)

type StrategyDecision struct {
	Type            DecisionType
	PositionSize    *decimal.Decimal
	Reason          string
	ConditionResult *bool
}

type DecisionInput struct {
	Strategy      domain.Strategy
	Klines        []domain.Candle
	Index         int
	Registry      *indicator.Registry
	InPosition    bool
	CurrentEquity decimal.Decimal
	CurrentPrice  decimal.Decimal
	PositionCount int
	AvgEntryPrice *decimal.Decimal
}

type StrategyDecisionEngine struct{}

func NewStrategyDecisionEngine() *StrategyDecisionEngine {
	return &StrategyDecisionEngine{}
}

func (sde *StrategyDecisionEngine) Decide(input DecisionInput) StrategyDecision {
	if !input.InPosition {
		return sde.evaluateEntry(input)
	}
	return sde.evaluateExit(input)
}

func (sde *StrategyDecisionEngine) evaluateEntry(input DecisionInput) StrategyDecision {
	if isVolatilityGrid(input.Strategy.ExecutionRule) {
		return sde.evaluateGridEntry(input)
	}

	if !hasConditions(input.Strategy.EntryCondition) {
		return StrategyDecision{Type: DecisionHold, Reason: "no_entry_condition"}
	}

	ctx := EvaluationContext{
		Index:    input.Index,
		Klines:   input.Klines,
		Registry: input.Registry,
	}
	entry, err := EvaluateCondition(input.Strategy.EntryCondition, ctx)
	condResult := entry && err == nil
	if !condResult {
		return StrategyDecision{
			Type:            DecisionHold,
			ConditionResult: &condResult,
		}
	}

	return StrategyDecision{
		Type:            DecisionEnter,
		PositionSize:    nil,
		Reason:          "entry_condition_met",
		ConditionResult: &condResult,
	}
}

func (sde *StrategyDecisionEngine) evaluateExit(input DecisionInput) StrategyDecision {
	if isVolatilityGrid(input.Strategy.ExecutionRule) {
		return sde.evaluateGridExit(input)
	}

	if !hasConditions(input.Strategy.ExitCondition) {
		return StrategyDecision{Type: DecisionHold, Reason: "no_exit_condition"}
	}

	ctx := EvaluationContext{
		Index:    input.Index,
		Klines:   input.Klines,
		Registry: input.Registry,
	}
	exit, err := EvaluateCondition(input.Strategy.ExitCondition, ctx)
	condResult := exit && err == nil
	if !condResult {
		return StrategyDecision{
			Type:            DecisionHold,
			ConditionResult: &condResult,
		}
	}

	return StrategyDecision{
		Type:            DecisionExit,
		Reason:          "exit_condition_met",
		ConditionResult: &condResult,
	}
}

func (sde *StrategyDecisionEngine) evaluateGridEntry(input DecisionInput) StrategyDecision {
	rule := parseGridRule(input.Strategy.ExecutionRule)
	if rule == nil {
		return StrategyDecision{Type: DecisionHold}
	}

	if input.PositionCount >= rule.MaxPyramidingLevels {
		return StrategyDecision{Type: DecisionHold, Reason: "max_pyramiding_reached"}
	}

	if input.AvgEntryPrice == nil {
		return StrategyDecision{
			Type:         DecisionEnter,
			PositionSize: decimalPtr(rule.BasePositionSize),
			Reason:       "grid_initial_entry",
		}
	}

	priceChange := input.CurrentPrice.Sub(*input.AvgEntryPrice).Div(*input.AvgEntryPrice)
	threshold := decimal.NewFromFloat(rule.RebalancePercent / 100.0)

	if priceChange.Abs().GreaterThanOrEqual(threshold) && priceChange.IsNegative() {
		return StrategyDecision{
			Type:         DecisionEnter,
			PositionSize: decimalPtr(rule.BasePositionSize),
			Reason:       "grid_rebalance_entry",
		}
	}

	return StrategyDecision{Type: DecisionHold}
}

func (sde *StrategyDecisionEngine) evaluateGridExit(input DecisionInput) StrategyDecision {
	rule := parseGridRule(input.Strategy.ExecutionRule)
	if rule == nil {
		return StrategyDecision{Type: DecisionHold}
	}

	if input.AvgEntryPrice == nil {
		return StrategyDecision{Type: DecisionHold}
	}

	priceChange := input.CurrentPrice.Sub(*input.AvgEntryPrice).Div(*input.AvgEntryPrice)
	threshold := decimal.NewFromFloat(rule.RebalancePercent / 100.0)

	if priceChange.Abs().GreaterThanOrEqual(threshold) && priceChange.IsPositive() {
		isLastLevel := input.PositionCount <= 1
		if isLastLevel {
			return StrategyDecision{
				Type:   DecisionExit,
				Reason: "grid_exit_all",
			}
		}
		return StrategyDecision{
			Type:         DecisionExit,
			PositionSize: decimalPtr(rule.BasePositionSize),
			Reason:       "grid_rebalance_exit",
		}
	}

	return StrategyDecision{Type: DecisionHold}
}

type GridRule struct {
	RebalancePercent    float64
	MaxPyramidingLevels int
	BasePositionSize    decimal.Decimal
}

func parseGridRule(rule json.RawMessage) *GridRule {
	if rule == nil || string(rule) == "null" || string(rule) == "{}" {
		return nil
	}

	var raw struct {
		Type                string           `json:"type"`
		RebalancePercent    *float64         `json:"rebalance_percent"`
		MaxPyramidingLevels *int             `json:"max_pyramiding_levels"`
		BasePositionSize    *decimal.Decimal `json:"base_position_size"`
	}
	if err := json.Unmarshal(rule, &raw); err != nil {
		return nil
	}

	if raw.Type != "volatility_grid" {
		return nil
	}

	gr := &GridRule{
		RebalancePercent:    1.0,
		MaxPyramidingLevels: 3,
		BasePositionSize:    decimal.NewFromInt(100),
	}

	if raw.RebalancePercent != nil {
		gr.RebalancePercent = *raw.RebalancePercent
	}
	if raw.MaxPyramidingLevels != nil {
		gr.MaxPyramidingLevels = *raw.MaxPyramidingLevels
	}
	if raw.BasePositionSize != nil {
		gr.BasePositionSize = *raw.BasePositionSize
	}

	return gr
}

func hasConditions(raw json.RawMessage) bool {
	if raw == nil || string(raw) == "null" || string(raw) == "{}" {
		return false
	}
	return true
}

func isVolatilityGrid(rule json.RawMessage) bool {
	return parseGridRule(rule) != nil
}

func decimalPtr(d decimal.Decimal) *decimal.Decimal {
	return &d
}
