package domain

import (
	"encoding/json"

	"github.com/shopspring/decimal"
)

type StrategyAction int

const (
	ActionHold StrategyAction = iota
	ActionEnter
	ActionExit
	ActionAddGrid
	ActionReduceGrid
)

type StrategyDecision struct {
	Action          StrategyAction  `json:"action"`
	QuoteSize       decimal.Decimal `json:"quoteSize"`
	Reason          string          `json:"reason"`
	ConditionResult *bool           `json:"conditionResult,omitempty"`
}

type DecisionInput struct {
	EntryCondition    json.RawMessage            `json:"entryCondition"`
	ExitCondition     json.RawMessage            `json:"exitCondition"`
	ExecutionRule     json.RawMessage            `json:"executionRule"`
	CurrentValues     map[string]decimal.Decimal `json:"currentValues"`
	PreviousValues    map[string]decimal.Decimal `json:"previousValues"`
	CurrentPrice      decimal.Decimal            `json:"currentPrice"`
	AverageEntryPrice decimal.Decimal            `json:"averageEntryPrice"`
	QuantityHeld      decimal.Decimal            `json:"quantityHeld"`
	LotCount          int                        `json:"lotCount"`
}

type RawGridRule struct {
	RebalancePercent    float64
	MaxPyramidingLevels int
	BasePositionSize    decimal.Decimal
}

func ParseExecutionRule(rule json.RawMessage) *RawGridRule {
	if len(rule) == 0 || string(rule) == "null" || string(rule) == "{}" {
		return nil
	}
	var probe struct {
		Type string `json:"type"`
	}
	if err := json.Unmarshal(rule, &probe); err != nil || probe.Type != "volatility_grid" {
		return nil
	}
	var raw struct {
		RebalancePercent    *float64         `json:"rebalancePercent"`
		MaxPyramidingLevels *int             `json:"maxPyramidingLevels"`
		BasePositionSize    *decimal.Decimal `json:"basePositionSize"`
	}
	if err := json.Unmarshal(rule, &raw); err != nil {
		return nil
	}
	r := &RawGridRule{
		RebalancePercent:    1.0,
		MaxPyramidingLevels: 3,
		BasePositionSize:    decimal.NewFromInt(100),
	}
	if raw.RebalancePercent != nil {
		r.RebalancePercent = *raw.RebalancePercent
	}
	if raw.MaxPyramidingLevels != nil {
		r.MaxPyramidingLevels = *raw.MaxPyramidingLevels
	}
	if raw.BasePositionSize != nil {
		r.BasePositionSize = *raw.BasePositionSize
	}
	return r
}

func HasCondition(cond json.RawMessage) bool {
	return len(cond) > 0 && string(cond) != "null" && string(cond) != "{}"
}

func GridDecide(rule *RawGridRule, input DecisionInput) StrategyDecision {
	hasPosition := input.QuantityHeld.IsPositive()

	if !hasPosition {
		return StrategyDecision{
			Action:    ActionAddGrid,
			QuoteSize: rule.BasePositionSize,
			Reason:    "grid_initial_entry",
		}
	}

	if !input.AverageEntryPrice.IsPositive() {
		return StrategyDecision{Action: ActionHold, Reason: "invalid_avg_price"}
	}

	priceChange := input.CurrentPrice.Sub(input.AverageEntryPrice).Div(input.AverageEntryPrice)
	threshold := decimal.NewFromFloat(rule.RebalancePercent / 100.0)

	if priceChange.Abs().GreaterThanOrEqual(threshold) && priceChange.IsNegative() {
		if input.LotCount >= rule.MaxPyramidingLevels {
			return StrategyDecision{Action: ActionHold, Reason: "max_pyramiding_reached"}
		}
		return StrategyDecision{
			Action:    ActionAddGrid,
			QuoteSize: rule.BasePositionSize,
			Reason:    "grid_rebalance_entry",
		}
	}

	if priceChange.Abs().GreaterThanOrEqual(threshold) && priceChange.IsPositive() {
		if input.LotCount <= 1 {
			return StrategyDecision{Action: ActionExit, Reason: "grid_exit_all"}
		}
		return StrategyDecision{
			Action:    ActionReduceGrid,
			QuoteSize: rule.BasePositionSize,
			Reason:    "grid_rebalance_exit",
		}
	}

	return StrategyDecision{Action: ActionHold, Reason: "no_grid_action"}
}
