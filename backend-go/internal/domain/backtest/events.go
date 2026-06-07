package backtest

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

type BacktestStartedEvent struct {
	TaskID       uuid.UUID
	StrategyName string
	Pair         string
	Timestamp    time.Time
}

func (BacktestStartedEvent) EventType() string { return "BacktestStartedEvent" }

type BacktestCompletedEvent struct {
	TaskID             uuid.UUID
	StrategyName       string
	Pair               string
	FinalValue         decimal.Decimal
	TotalReturnPercent decimal.Decimal
	Timestamp          time.Time
}

func (BacktestCompletedEvent) EventType() string { return "BacktestCompletedEvent" }

type BacktestFailedEvent struct {
	TaskID       uuid.UUID
	StrategyName string
	Pair         string
	Reason       string
	Timestamp    time.Time
}

func (BacktestFailedEvent) EventType() string { return "BacktestFailedEvent" }

type BacktestCancelledEvent struct {
	TaskID       uuid.UUID
	StrategyName string
	Pair         string
	Timestamp    time.Time
}

func (BacktestCancelledEvent) EventType() string { return "BacktestCancelledEvent" }
