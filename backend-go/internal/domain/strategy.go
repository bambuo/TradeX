package domain

import (
	"encoding/json"
	"time"

	"github.com/google/uuid"
)

type Strategy struct {
	ID             uuid.UUID
	Name           string
	EntryCondition json.RawMessage
	ExitCondition  json.RawMessage
	ExecutionRule  json.RawMessage
	Version        int
	CreatedBy      uuid.UUID
	CreatedAt      time.Time
	UpdatedAt      time.Time
}
