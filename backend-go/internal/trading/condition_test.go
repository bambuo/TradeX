package trading

import (
	"testing"

	"github.com/shopspring/decimal"
)

func TestConditionEvaluator_EmptyJSON(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"close": dn("100")}
	prev := map[string]decimal.Decimal{"close": dn("99")}

	if e.Evaluate([]byte(""), cur, prev) {
		t.Error("空 JSON 应返回 false")
	}
	if e.Evaluate([]byte("{}"), cur, prev) {
		t.Error("{} 应返回 false")
	}
	if e.Evaluate([]byte("null"), cur, prev) {
		t.Error("null 应返回 false")
	}
}

func TestConditionEvaluator_GreaterThan(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"sma_20": dn("51000"), "close": dn("52000")}
	prev := map[string]decimal.Decimal{"sma_20": dn("50000"), "close": dn("51000")}

	tests := []struct {
		name string
		json string
		want bool
	}{
		{"close > 51000", `{"indicator":"close","comparison":">","value":51000}`, true},
		{"close > 53000", `{"indicator":"close","comparison":">","value":53000}`, false},
		{"sma >= 51000", `{"indicator":"sma_20","comparison":">=","value":51000}`, true},
		{"close < 51000", `{"indicator":"close","comparison":"<","value":51000}`, false},
		{"close <= 52000", `{"indicator":"close","comparison":"<=","value":52000}`, true},
		{"close == 52000", `{"indicator":"close","comparison":"==","value":52000}`, true},
		{"close == 52001", `{"indicator":"close","comparison":"==","value":52001}`, false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := e.Evaluate([]byte(tt.json), cur, prev)
			if got != tt.want {
				t.Errorf("Evaluate(%q) = %v, want %v", tt.json, got, tt.want)
			}
		})
	}
}

func TestConditionEvaluator_CrossAbove(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"sma_20": dn("50500"), "close": dn("51000")}
	prev := map[string]decimal.Decimal{"sma_20": dn("50000"), "close": dn("49900")}
	// 无穿越：prev 已经在上方且 curr 仍在 compareValue 之上
	noCrossCur := map[string]decimal.Decimal{"sma_20": dn("50000"), "close": dn("52000")}
	noCrossPrev := map[string]decimal.Decimal{"sma_20": dn("50000"), "close": dn("51000")}
	bothBelow := map[string]decimal.Decimal{"sma_20": dn("50000"), "close": dn("49900")}

	tests := []struct {
		name      string
		json      string
		cur, prev map[string]decimal.Decimal
		want      bool
	}{
		{"close CA sma_20 (value=1)",
			`{"indicator":"close","comparison":"CA","value":1,"ref":"sma_20"}`,
			cur, prev, true},
		{"close CA sma_20 no x",
			`{"indicator":"close","comparison":"CA","value":1,"ref":"sma_20"}`,
			noCrossCur, noCrossPrev, false},
		{"close CA sma_20 both above",
			`{"indicator":"close","comparison":"CA","value":1,"ref":"sma_20"}`,
			cur, cur, false},
		{"close CA sma_20 both below",
			`{"indicator":"close","comparison":"CA","value":1,"ref":"sma_20"}`,
			bothBelow, bothBelow, false},
		{"close CA 50000 (no ref)",
			`{"indicator":"close","comparison":"CA","value":50000}`,
			cur, prev, true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := e.Evaluate([]byte(tt.json), tt.cur, tt.prev)
			if got != tt.want {
				t.Errorf("Evaluate() = %v, want %v", got, tt.want)
			}
		})
	}
}

func TestConditionEvaluator_CrossBelow(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"rsi_14": dn("30"), "close": dn("49000")}
	prev := map[string]decimal.Decimal{"rsi_14": dn("45"), "close": dn("50000")}

	tests := []struct {
		name      string
		json      string
		cur, prev map[string]decimal.Decimal
		want      bool
	}{
		{"close CB 50000", `{"indicator":"close","comparison":"CB","value":50000}`, cur, prev, true},
		{"close CB 50000 no x", `{"indicator":"close","comparison":"CB","value":50000}`, prev, prev, false},
		{"close CB 50000 both below", `{"indicator":"close","comparison":"CB","value":50000}`, cur, cur, false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := e.Evaluate([]byte(tt.json), tt.cur, tt.prev)
			if got != tt.want {
				t.Errorf("Evaluate() = %v, want %v", got, tt.want)
			}
		})
	}
}

func TestConditionEvaluator_AND_OR_NOT(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"a": dn("10"), "b": dn("20"), "c": dn("30")}
	prev := map[string]decimal.Decimal{"a": dn("5"), "b": dn("15"), "c": dn("25")}

	tests := []struct {
		name string
		json string
		want bool
	}{
		{"AND all true",
			`{"operator":"AND","conditions":[
				{"indicator":"a","comparison":">","value":5},
				{"indicator":"b","comparison":">","value":15}
			]}`, true},
		{"AND one false",
			`{"operator":"AND","conditions":[
				{"indicator":"a","comparison":">","value":10},
				{"indicator":"b","comparison":">","value":15}
			]}`, false},
		{"OR one true",
			`{"operator":"OR","conditions":[
				{"indicator":"a","comparison":">","value":100},
				{"indicator":"b","comparison":">","value":15}
			]}`, true},
		{"OR all false",
			`{"operator":"OR","conditions":[
				{"indicator":"a","comparison":">","value":100},
				{"indicator":"b","comparison":">","value":50}
			]}`, false},
		{"OR empty", `{"operator":"OR","conditions":[]}`, false},
		{"NOT true", `{"operator":"NOT","conditions":[
			{"indicator":"a","comparison":">","value":100}
		]}`, true},
		{"NOT nested AND",
			`{"operator":"NOT","conditions":[
				{"operator":"AND","conditions":[
					{"indicator":"a","comparison":">","value":5},
					{"indicator":"b","comparison":">","value":100}
				]}
			]}`, true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := e.Evaluate([]byte(tt.json), cur, prev)
			if got != tt.want {
				t.Errorf("Evaluate(%q) = %v, want %v", tt.name, got, tt.want)
			}
		})
	}
}

func TestConditionEvaluator_Ref(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"rsi_14": dn("70"), "close": dn("52000")}
	prev := map[string]decimal.Decimal{"rsi_14": dn("65"), "close": dn("51000")}

	tests := []struct {
		name string
		json string
		want bool
	}{
		{"rsi*1.5 ref", `{"indicator":"rsi_14","comparison":">","value":1.5,"ref":"rsi_14"}`, false},
		{"close > rsi*0.5", `{"indicator":"close","comparison":">","value":0.5,"ref":"rsi_14"}`, true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := e.Evaluate([]byte(tt.json), cur, prev)
			if got != tt.want {
				t.Errorf("Evaluate(%q) = %v, want %v", tt.name, got, tt.want)
			}
		})
	}
}

func TestConditionEvaluator_MissingIndicator(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"close": dn("100")}
	prev := map[string]decimal.Decimal{}

	if e.Evaluate([]byte(`{"indicator":"rsi_14","comparison":">","value":50}`), cur, prev) {
		t.Error("不存在的指标应返回 false")
	}
}

func TestConditionEvaluator_AND_Empty(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"close": dn("100")}
	prev := map[string]decimal.Decimal{}

	if !e.Evaluate([]byte(`{"operator":"AND","conditions":[]}`), cur, prev) {
		t.Error("AND 空数组应为 true 空真语义")
	}
}

func TestConditionEvaluator_UnknownOperator(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"close": dn("100")}
	prev := map[string]decimal.Decimal{}

	if e.Evaluate([]byte(`{"operator":"INVALID","conditions":[]}`), cur, prev) {
		t.Error("未知操作符应返回 false")
	}
}

func TestConditionEvaluator_BadJSON(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{"close": dn("100")}
	prev := map[string]decimal.Decimal{}

	if e.Evaluate([]byte(`{bad json`), cur, prev) {
		t.Error("损坏 JSON 应返回 false 不 panic")
	}
}

func TestConditionTreeValidator(t *testing.T) {
	v := NewConditionTreeValidator([]string{"sma_20", "rsi_14", "close"})

	tests := []struct {
		name     string
		json     string
		wantErrs int
	}{
		{"valid leaf", `{"indicator":"close","comparison":">","value":50000}`, 0},
		{"valid AND", `{"operator":"AND","conditions":[
			{"indicator":"sma_20","comparison":">","value":100}
		]}`, 0},
		{"missing comparison", `{"indicator":"close","value":50000}`, 1},
		{"unregistered indicator", `{"indicator":"fake","comparison":">","value":100}`, 1},
		{"bad operator", `{"operator":"XOR","conditions":[]}`, 1},
		{"empty JSON", `{}`, 0},
		{"null", `null`, 0},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			issues := v.Validate([]byte(tt.json))
			if len(issues) != tt.wantErrs {
				t.Errorf("Validate() = %d issues, want %d; got %+v", len(issues), tt.wantErrs, issues)
			}
		})
	}
}

func TestCrossAbove_BothMissing(t *testing.T) {
	e := NewConditionEvaluator()
	cur := map[string]decimal.Decimal{}
	prev := map[string]decimal.Decimal{}

	if e.Evaluate([]byte(`{"indicator":"close","comparison":"CA","value":100}`), cur, prev) {
		t.Error("指标缺失时 CA 应返回 false")
	}
}
