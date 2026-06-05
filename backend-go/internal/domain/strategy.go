package domain

import (
	"encoding/json"

	"github.com/google/uuid"
)

type Strategy struct {
	ID             uuid.UUID
	Name           string
	EntryCondition json.RawMessage
	ExitCondition  json.RawMessage
	ExecutionRule  json.RawMessage
	ExchangeID     string
	Pair           string
	Timeframe      string
	IsActive       bool
}
