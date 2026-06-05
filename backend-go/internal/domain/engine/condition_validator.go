package engine

import (
	"encoding/json"
	"fmt"
)

func ValidateCondition(raw json.RawMessage) error {
	if raw == nil || string(raw) == "null" {
		return fmt.Errorf("condition is nil")
	}

	var node ConditionNode
	if err := json.Unmarshal(raw, &node); err != nil {
		return fmt.Errorf("invalid JSON: %w", err)
	}

	return validateNode(&node)
}

func validateNode(node *ConditionNode) error {
	if node == nil {
		return fmt.Errorf("nil condition node")
	}

	switch node.Type {
	case "comparison":
		if node.Operator == "" {
			return fmt.Errorf("comparison node missing operator")
		}
		if node.Left == nil {
			return fmt.Errorf("comparison node missing left operand")
		}
		if node.Right == nil {
			return fmt.Errorf("comparison node missing right operand")
		}
		return nil

	case "and", "or":
		if len(node.Conditions) == 0 {
			return fmt.Errorf("%s node must have at least one condition", node.Type)
		}
		for _, c := range node.Conditions {
			if err := validateNode(c); err != nil {
				return err
			}
		}
		return nil

	case "not":
		if len(node.Conditions) == 0 {
			return fmt.Errorf("not node must have at least one condition")
		}
		return validateNode(node.Conditions[0])

	default:
		return fmt.Errorf("unknown condition type: %s", node.Type)
	}
}
