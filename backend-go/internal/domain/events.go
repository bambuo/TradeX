package domain

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

type BacktestStartedEvent struct {
	TaskID    uuid.UUID
	Timestamp time.Time
}

type BacktestCompletedEvent struct {
	TaskID              uuid.UUID
	FinalValue          decimal.Decimal
	TotalReturnPercent  decimal.Decimal
	Timestamp           time.Time
}

type BacktestFailedEvent struct {
	TaskID    uuid.UUID
	Reason    string
	Timestamp time.Time
}

type BacktestCancelledEvent struct {
	TaskID    uuid.UUID
	Timestamp time.Time
}
