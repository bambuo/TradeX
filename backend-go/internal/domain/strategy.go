package domain

import (
	"encoding/json"
	"fmt"
	"time"

	"github.com/google/uuid"
)

type Strategy struct {
	ID             uuid.UUID       `json:"id"`
	Name           string          `json:"name"`
	EntryCondition json.RawMessage `json:"entryCondition,omitempty"`
	ExitCondition  json.RawMessage `json:"exitCondition,omitempty"`
	ExecutionRule  json.RawMessage `json:"executionRule,omitempty"`
	Version        int             `json:"version"`
	CreatedBy      uuid.UUID       `json:"createdBy"`
	CreatedAt      time.Time       `json:"createdAt"`
	UpdatedAt      time.Time       `json:"updatedAt"`
}

func (s *Strategy) Validate() error {
	if s.Name == "" {
		return fmt.Errorf("strategy name cannot be empty")
	}
	if len(s.EntryCondition) == 0 {
		return fmt.Errorf("entry condition cannot be empty")
	}
	return nil
}
