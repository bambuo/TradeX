package engine

import (
	"encoding/json"
	"fmt"
	"strings"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

type EvaluationContext struct {
	Index    int
	Klines   []domain.Candle
	Registry *indicator.Registry
}

type ConditionNode struct {
	Type       string           `json:"type,omitempty"`
	Left       *ConditionNode   `json:"left,omitempty"`
	Right      *ConditionNode   `json:"right,omitempty"`
	Operator   string           `json:"operator,omitempty"`
	Conditions []*ConditionNode `json:"conditions,omitempty"`
	Ref        *RefNode         `json:"-"`
	RefRaw     json.RawMessage  `json:"ref,omitempty"`
	Value      *float64         `json:"value,omitempty"`
	Name       string           `json:"name,omitempty"`
	Period     *int             `json:"period,omitempty"`

	Indicator  string `json:"indicator,omitempty"`
	Comparison string `json:"comparison,omitempty"`
}

type RefNode struct {
	Name   string `json:"name"`
	Period *int   `json:"period,omitempty"`
	Value  string `json:"value,omitempty"` // "close" | "open" | "high" | "low"
}

func EvaluateCondition(raw json.RawMessage, ctx EvaluationContext) (bool, error) {
	if raw == nil || string(raw) == "null" || string(raw) == "{}" || string(raw) == "" {
		return false, nil
	}

	node, err := unmarshalConditionNode(raw)
	if err != nil {
		return false, nil
	}

	return evaluateNode(node, ctx)
}

func unmarshalConditionNode(raw json.RawMessage) (*ConditionNode, error) {
	var node ConditionNode
	if err := json.Unmarshal(raw, &node); err != nil {
		return nil, err
	}

	if node.RefRaw != nil {
		var refStr string
		if err := json.Unmarshal(node.RefRaw, &refStr); err == nil {
			node.Ref = &RefNode{Name: refStr}
		} else {
			var refObj RefNode
			if err := json.Unmarshal(node.RefRaw, &refObj); err == nil {
				node.Ref = &refObj
			}
		}
	}

	return &node, nil
}

func evaluateNode(node *ConditionNode, ctx EvaluationContext) (bool, error) {
	if node.Indicator != "" && node.Comparison != "" {
		return evalLegacyComparison(node, ctx)
	}

	switch node.Type {
	case "comparison":
		return evalComparison(node, ctx)
	case "and":
		return evalAnd(node, ctx)
	case "or":
		return evalOr(node, ctx)
	case "not":
		return evalNot(node, ctx)
	case "":
		return false, nil
	default:
		return false, fmt.Errorf("unknown condition type: %s", node.Type)
	}
}

func evalLegacyComparison(node *ConditionNode, ctx EvaluationContext) (bool, error) {
	leftVal, err := resolveIndicator(node.Indicator, ctx)
	if err != nil {
		return false, err
	}

	var rightVal, prevRightVal float64
	if node.Ref != nil && node.Ref.Name != "" {
		rightVal, err = resolveIndicator(node.Ref.Name, ctx)
		if err != nil {
			return false, err
		}
		rightVal *= *node.Value
		// crossover 需要上一根的 ref 值
		if ctx.Index > 0 {
			prevCtx := ctx
			prevCtx.Index = ctx.Index - 1
			prevRightVal, err = resolveIndicator(node.Ref.Name, prevCtx)
			if err != nil {
				return false, err
			}
			prevRightVal *= *node.Value
		} else {
			prevRightVal = rightVal
		}
	} else if node.Value != nil {
		rightVal = *node.Value
		prevRightVal = rightVal
	} else {
		return false, fmt.Errorf("legacy condition missing value or ref")
	}

	switch node.Comparison {
	case ">":
		return leftVal > rightVal, nil
	case "<":
		return leftVal < rightVal, nil
	case ">=":
		return leftVal >= rightVal, nil
	case "<=":
		return leftVal <= rightVal, nil
	case "==", "=":
		return leftVal == rightVal, nil
	case "!=":
		return leftVal != rightVal, nil
	case "CA":
		return evalCrossOverLegacy(node.Indicator, rightVal, prevRightVal, ctx)
	case "CB":
		return evalCrossUnderLegacy(node.Indicator, rightVal, prevRightVal, ctx)
	default:
		return false, fmt.Errorf("unknown legacy comparison: %s", node.Comparison)
	}
}

func resolveIndicator(name string, ctx EvaluationContext) (float64, error) {
	name = strings.ToLower(name)
	if val, ok := ctx.Registry.GetValue(name, ctx.Index); ok {
		return val, nil
	}
	closeVal, _ := ctx.Klines[ctx.Index].Close.Float64()
	return closeVal, nil
}

func evalCrossOverLegacy(indicator string, currCompare, prevCompare float64, ctx EvaluationContext) (bool, error) {
	if ctx.Index < 1 {
		return false, nil
	}
	curr, _ := resolveIndicator(indicator, ctx)
	prev, _ := resolveIndicator(indicator, EvaluationContext{Index: ctx.Index - 1, Klines: ctx.Klines, Registry: ctx.Registry})
	return prev <= prevCompare && curr > currCompare, nil
}

func evalCrossUnderLegacy(indicator string, currCompare, prevCompare float64, ctx EvaluationContext) (bool, error) {
	if ctx.Index < 1 {
		return false, nil
	}
	curr, _ := resolveIndicator(indicator, ctx)
	prev, _ := resolveIndicator(indicator, EvaluationContext{Index: ctx.Index - 1, Klines: ctx.Klines, Registry: ctx.Registry})
	return prev >= prevCompare && curr < currCompare, nil
}

func evalComparison(node *ConditionNode, ctx EvaluationContext) (bool, error) {
	left, err := resolveValue(node.Left, ctx)
	if err != nil {
		return false, err
	}
	right, err := resolveValue(node.Right, ctx)
	if err != nil {
		return false, err
	}

	switch node.Operator {
	case ">":
		return left > right, nil
	case "<":
		return left < right, nil
	case ">=":
		return left >= right, nil
	case "<=":
		return left <= right, nil
	case "==", "=":
		return left == right, nil
	case "!=":
		return left != right, nil
	case "cross_over":
		return evalCrossOver(node.Left, node.Right, ctx)
	case "cross_under":
		return evalCrossUnder(node.Left, node.Right, ctx)
	default:
		return false, fmt.Errorf("unknown operator: %s", node.Operator)
	}
}

func evalAnd(node *ConditionNode, ctx EvaluationContext) (bool, error) {
	for _, c := range node.Conditions {
		result, err := evaluateNode(c, ctx)
		if err != nil {
			return false, err
		}
		if !result {
			return false, nil
		}
	}
	return true, nil
}

func evalOr(node *ConditionNode, ctx EvaluationContext) (bool, error) {
	for _, c := range node.Conditions {
		result, err := evaluateNode(c, ctx)
		if err != nil {
			return false, err
		}
		if result {
			return true, nil
		}
	}
	return false, nil
}

func evalNot(node *ConditionNode, ctx EvaluationContext) (bool, error) {
	if len(node.Conditions) == 0 {
		return false, fmt.Errorf("not node requires at least one condition")
	}
	result, err := evaluateNode(node.Conditions[0], ctx)
	if err != nil {
		return false, err
	}
	return !result, nil
}

func evalCrossOver(left, right *ConditionNode, ctx EvaluationContext) (bool, error) {
	if ctx.Index < 1 {
		return false, nil
	}

	prevCtx := ctx
	prevCtx.Index = ctx.Index - 1

	currLeft, _ := resolveValue(left, ctx)
	currRight, _ := resolveValue(right, ctx)
	prevLeft, _ := resolveValue(left, prevCtx)
	prevRight, _ := resolveValue(right, prevCtx)

	return prevLeft <= prevRight && currLeft > currRight, nil
}

func evalCrossUnder(left, right *ConditionNode, ctx EvaluationContext) (bool, error) {
	if ctx.Index < 1 {
		return false, nil
	}

	prevCtx := ctx
	prevCtx.Index = ctx.Index - 1

	currLeft, _ := resolveValue(left, ctx)
	currRight, _ := resolveValue(right, ctx)
	prevLeft, _ := resolveValue(left, prevCtx)
	prevRight, _ := resolveValue(right, prevCtx)

	return prevLeft >= prevRight && currLeft < currRight, nil
}

func resolveValue(node *ConditionNode, ctx EvaluationContext) (float64, error) {
	if node == nil {
		return 0, fmt.Errorf("nil node")
	}

	if node.Value != nil {
		return *node.Value, nil
	}

	if node.Ref != nil {
		return resolveRef(node.Ref, ctx)
	}

	if node.Name != "" {
		return resolveRef(&RefNode{Name: node.Name, Period: node.Period}, ctx)
	}

	return resolveValueSimple(node, ctx)
}

func resolveValueSimple(node *ConditionNode, ctx EvaluationContext) (float64, error) {
	if node.Ref != nil {
		return resolveRef(node.Ref, ctx)
	}
	return 0, fmt.Errorf("cannot resolve value from node: %+v", node)
}

func resolveRef(ref *RefNode, ctx EvaluationContext) (float64, error) {
	name := strings.ToLower(ref.Name)

	switch name {
	case "close":
		v, _ := ctx.Klines[ctx.Index].Close.Float64()
		return v, nil
	case "open":
		v, _ := ctx.Klines[ctx.Index].Open.Float64()
		return v, nil
	case "high":
		v, _ := ctx.Klines[ctx.Index].High.Float64()
		return v, nil
	case "low":
		v, _ := ctx.Klines[ctx.Index].Low.Float64()
		return v, nil
	case "volume":
		v, _ := ctx.Klines[ctx.Index].Volume.Float64()
		return v, nil
	default:
		if val, ok := ctx.Registry.GetValue(name, ctx.Index); ok {
			return val, nil
		}
		return 0, fmt.Errorf("unknown ref: %s", name)
	}
}
