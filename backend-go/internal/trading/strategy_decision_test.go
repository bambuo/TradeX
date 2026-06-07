package trading

import (
	"encoding/json"
	"testing"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

func j(s string) json.RawMessage { return json.RawMessage(s) }

func TestStrategyDecisionEngine_ConditionEntry(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		EntryCondition: j(`{"indicator":"close","comparison":">","value":50000}`),
		ExitCondition:  j(`{}`),
		CurrentValues:  map[string]decimal.Decimal{"close": dn("51000")},
		PreviousValues: map[string]decimal.Decimal{"close": dn("49000")},
		CurrentPrice:   dn("51000"),
		QuantityHeld:   decimal.Zero,
	}

	dec := engine.Decide(input)
	if dec.Action != domain.ActionEnter {
		t.Errorf("应入场，实际 Action=%v Reason=%s", dec.Action, dec.Reason)
	}
}

func TestStrategyDecisionEngine_ConditionNoEntry(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		EntryCondition: j(`{"indicator":"close","comparison":">","value":60000}`),
		CurrentValues:  map[string]decimal.Decimal{"close": dn("51000")},
		PreviousValues: map[string]decimal.Decimal{"close": dn("49000")},
		CurrentPrice:   dn("51000"),
		QuantityHeld:   decimal.Zero,
	}

	dec := engine.Decide(input)
	if dec.Action != domain.ActionHold {
		t.Errorf("不应入场，实际 Action=%v", dec.Action)
	}
}

func TestStrategyDecisionEngine_ConditionExit(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		EntryCondition: j(`{}`),
		ExitCondition:  j(`{"indicator":"close","comparison":"<","value":49000}`),
		CurrentValues:  map[string]decimal.Decimal{"close": dn("48000")},
		PreviousValues: map[string]decimal.Decimal{"close": dn("50000")},
		CurrentPrice:   dn("48000"),
		QuantityHeld:   dn("1"),
	}

	dec := engine.Decide(input)
	if dec.Action != domain.ActionExit {
		t.Errorf("应出场，实际 Action=%v Reason=%s", dec.Action, dec.Reason)
	}
}

func TestStrategyDecisionEngine_HoldInPosition(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		EntryCondition: j(`{}`),
		ExitCondition:  j(`{"indicator":"close","comparison":">","value":60000}`),
		CurrentValues:  map[string]decimal.Decimal{"close": dn("50000")},
		PreviousValues: map[string]decimal.Decimal{"close": dn("49000")},
		CurrentPrice:   dn("50000"),
		QuantityHeld:   dn("1"),
	}

	dec := engine.Decide(input)
	if dec.Action != domain.ActionHold {
		t.Errorf("应持有，实际 Action=%v", dec.Action)
	}
}

func TestStrategyDecisionEngine_VolatilityGridInitialEntry(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		ExecutionRule: j(`{"type":"volatility_grid","basePositionSize":100,"maxPyramidingLevels":5}`),
		CurrentPrice:  dn("50000"),
		QuantityHeld:  decimal.Zero,
	}

	dec := engine.Decide(input)
	if dec.Action != domain.ActionAddGrid {
		t.Errorf("网格首仓应入场，实际 Action=%v", dec.Action)
	}
	if !dec.QuoteSize.Equal(dn("100")) {
		t.Errorf("BasePositionSize 应为 100，实际 %s", dec.QuoteSize.String())
	}
}

func TestStrategyDecisionEngine_VolatilityGridRebalanceBuy(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		ExecutionRule:     j(`{"type":"volatility_grid","basePositionSize":10,"maxPyramidingLevels":5,"rebalancePercent":1,"maxPositionSize":10000}`),
		CurrentPrice:      dn("45"),
		AverageEntryPrice: dn("50"),
		QuantityHeld:      dn("1"),
		LotCount:          1,
	}
	dec := engine.Decide(input)
	if dec.Action != domain.ActionAddGrid {
		t.Errorf("下跌触发加仓，实际 Action=%v Reason=%s", dec.Action, dec.Reason)
	}
}

func TestStrategyDecisionEngine_VolatilityGridRebalanceSell(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		ExecutionRule:     j(`{"type":"volatility_grid","basePositionSize":10,"maxPyramidingLevels":5,"rebalancePercent":1,"maxPositionSize":10000}`),
		CurrentPrice:      dn("55"),
		AverageEntryPrice: dn("50"),
		QuantityHeld:      dn("1"),
		LotCount:          1,
	}
	dec := engine.Decide(input)
	if dec.Action != domain.ActionReduceGrid {
		t.Errorf("上涨触发减仓，实际 Action=%v Reason=%s", dec.Action, dec.Reason)
	}
}

func TestStrategyDecisionEngine_NoEntryCondition(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		EntryCondition: j(`{}`),
		CurrentPrice:   dn("50000"),
		QuantityHeld:   decimal.Zero,
	}

	dec := engine.Decide(input)
	if dec.Action != domain.ActionHold {
		t.Errorf("无入场条件应 hold，实际 Action=%v", dec.Action)
	}
}

func TestStrategyDecisionEngine_NoExitCondition(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		EntryCondition: j(`{}`),
		ExitCondition:  j(`{}`),
		CurrentPrice:   dn("50000"),
		QuantityHeld:   dn("1"),
	}

	dec := engine.Decide(input)
	if dec.Action != domain.ActionHold {
		t.Errorf("无出场条件应 hold，实际 Action=%v", dec.Action)
	}
}

func TestStrategyDecisionEngine_GridMaxLevel(t *testing.T) {
	e := NewConditionEvaluator()
	engine := NewStrategyDecisionEngine(e)

	input := domain.DecisionInput{
		ExecutionRule:     j(`{"type":"volatility_grid","basePositionSize":100,"maxPyramidingLevels":2,"rebalancePercent":1}`),
		CurrentPrice:      dn("40000"),
		AverageEntryPrice: dn("50000"),
		QuantityHeld:      dn("3"),
		LotCount:          2,
	}
	dec := engine.Decide(input)
	if dec.Action != domain.ActionHold {
		t.Errorf("已达加仓上限应 hold，实际 Action=%v", dec.Action)
	}
}
