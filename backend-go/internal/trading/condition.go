package trading

import (
	"encoding/json"
	"fmt"

	"github.com/shopspring/decimal"
)

type conditionNode struct {
	Operator   string           `json:"operator,omitempty"`
	Conditions []*conditionNode `json:"conditions,omitempty"`
	Indicator  string           `json:"indicator,omitempty"`
	Comparison string           `json:"comparison,omitempty"`
	Value      *decimal.Decimal `json:"value,omitempty"`
	Ref        *string          `json:"ref,omitempty"`
}

type ConditionEvaluator struct{}

func NewConditionEvaluator() *ConditionEvaluator {
	return &ConditionEvaluator{}
}

func (e *ConditionEvaluator) Evaluate(conditionJSON []byte, current, previous map[string]decimal.Decimal) bool {
	if len(conditionJSON) == 0 || string(conditionJSON) == "{}" || string(conditionJSON) == "null" {
		return false
	}
	var root conditionNode
	if err := json.Unmarshal(conditionJSON, &root); err != nil {
		return false
	}
	return e.evaluateNode(&root, current, previous)
}

func (e *ConditionEvaluator) evaluateNode(node *conditionNode, current, previous map[string]decimal.Decimal) bool {
	if node == nil {
		return false
	}
	if node.Indicator != "" && node.Comparison != "" {
		return e.evaluateLeaf(node, current, previous)
	}
	switch node.Operator {
	case "AND":
		for _, c := range node.Conditions {
			if !e.evaluateNode(c, current, previous) {
				return false
			}
		}
		return true
	case "OR":
		if len(node.Conditions) == 0 {
			return false
		}
		for _, c := range node.Conditions {
			if e.evaluateNode(c, current, previous) {
				return true
			}
		}
		return false
	case "NOT":
		if len(node.Conditions) == 1 {
			return !e.evaluateNode(node.Conditions[0], current, previous)
		}
		return false
	default:
		return false
	}
}

var epsilon = decimal.NewFromFloat(1e-12)

func (e *ConditionEvaluator) evaluateLeaf(node *conditionNode, current, previous map[string]decimal.Decimal) bool {
	actual, ok := current[node.Indicator]
	if !ok {
		return false
	}

	compareValue := decimal.Zero
	if node.Value != nil {
		compareValue = *node.Value
	}
	prevCompare := compareValue

	if node.Ref != nil && *node.Ref != "" {
		refVal, ok := current[*node.Ref]
		if !ok {
			return false
		}
		refMul := decimal.NewFromInt(1)
		if node.Value != nil {
			refMul = *node.Value
		}
		compareValue = refVal.Mul(refMul)

		prevRefVal, ok := previous[*node.Ref]
		if ok {
			prevCompare = prevRefVal.Mul(refMul)
		}
	}

	switch node.Comparison {
	case ">":
		return actual.GreaterThan(compareValue)
	case "<":
		return actual.LessThan(compareValue)
	case ">=":
		return actual.GreaterThanOrEqual(compareValue)
	case "<=":
		return actual.LessThanOrEqual(compareValue)
	case "==":
		return actual.Sub(compareValue).Abs().LessThan(epsilon)
	case "CA":
		return e.isCrossAbove(node.Indicator, compareValue, prevCompare, current, previous)
	case "CB":
		return e.isCrossBelow(node.Indicator, compareValue, prevCompare, current, previous)
	default:
		return false
	}
}

func (e *ConditionEvaluator) isCrossAbove(indicator string, currCompare, prevCompare decimal.Decimal, current, previous map[string]decimal.Decimal) bool {
	curr, ok1 := current[indicator]
	prev, ok2 := previous[indicator]
	if !ok1 || !ok2 {
		return false
	}
	prevVal, _ := prev.Float64()
	prevCmp, _ := prevCompare.Float64()
	currVal, _ := curr.Float64()
	currCmp, _ := currCompare.Float64()
	ca := prevVal <= prevCmp+1e-12 && currVal > currCmp
	return ca
}

func (e *ConditionEvaluator) isCrossBelow(indicator string, currCompare, prevCompare decimal.Decimal, current, previous map[string]decimal.Decimal) bool {
	curr, ok1 := current[indicator]
	prev, ok2 := previous[indicator]
	if !ok1 || !ok2 {
		return false
	}
	prevVal, _ := prev.Float64()
	prevCmp, _ := prevCompare.Float64()
	currVal, _ := curr.Float64()
	currCmp, _ := currCompare.Float64()
	cb := prevVal >= prevCmp-1e-12 && currVal < currCmp
	return cb
}

type ValidationIssue struct {
	Path    string
	Message string
}

type ConditionTreeValidator struct {
	validIndicators map[string]bool
}

func NewConditionTreeValidator(validIndicators []string) *ConditionTreeValidator {
	m := make(map[string]bool, len(validIndicators))
	for _, name := range validIndicators {
		m[name] = true
	}
	return &ConditionTreeValidator{validIndicators: m}
}

var validComparisons = map[string]bool{
	">": true, "<": true, ">=": true, "<=": true, "==": true, "CA": true, "CB": true,
}

var validGroupOps = map[string]bool{
	"AND": true, "OR": true, "NOT": true,
}

func (v *ConditionTreeValidator) Validate(conditionJSON []byte) []ValidationIssue {
	if len(conditionJSON) == 0 || string(conditionJSON) == "{}" || string(conditionJSON) == "null" {
		return nil
	}
	var root conditionNode
	if err := json.Unmarshal(conditionJSON, &root); err != nil {
		return []ValidationIssue{{Path: "$", Message: "JSON 解析失败: " + err.Error()}}
	}
	var issues []ValidationIssue
	v.validateNode(&root, "$", &issues)
	return issues
}

func (v *ConditionTreeValidator) validateNode(node *conditionNode, path string, issues *[]ValidationIssue) {
	if node == nil {
		return
	}
	if node.Indicator != "" || node.Comparison != "" {
		v.validateLeaf(node, path, issues)
		return
	}
	if node.Operator != "" && !validGroupOps[node.Operator] {
		*issues = append(*issues, ValidationIssue{Path: path + ".operator", Message: "不支持的运算符: " + node.Operator})
		return
	}
	for i, c := range node.Conditions {
		v.validateNode(c, fmt.Sprintf("%s.conditions[%d]", path, i), issues)
	}
}

func (v *ConditionTreeValidator) validateLeaf(node *conditionNode, path string, issues *[]ValidationIssue) {
	if node.Indicator == "" {
		*issues = append(*issues, ValidationIssue{Path: path + ".indicator", Message: "叶节点必须指定 indicator"})
	} else if !v.validIndicators[node.Indicator] {
		*issues = append(*issues, ValidationIssue{Path: path + ".indicator", Message: "指标 '" + node.Indicator + "' 未注册"})
	}
	if node.Comparison == "" || !validComparisons[node.Comparison] {
		*issues = append(*issues, ValidationIssue{Path: path + ".comparison", Message: "叶节点必须指定有效的 comparison"})
	}
	if node.Value == nil && (node.Ref == nil || *node.Ref == "") {
		*issues = append(*issues, ValidationIssue{Path: path + ".value", Message: "叶节点必须指定 value 或 ref"})
	}
}
