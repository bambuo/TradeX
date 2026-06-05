package domain

import (
	"time"

	"github.com/shopspring/decimal"
)

type BacktestKlineAnalysis struct {
	KlineIndex int
	Timestamp  time.Time

	Open   decimal.Decimal
	High   decimal.Decimal
	Low    decimal.Decimal
	Close  decimal.Decimal
	Volume decimal.Decimal

	IndicatorValues      map[string]float64
	EntryConditionResult map[string]any
	ExitConditionResult  map[string]any

	InPosition bool
	Action     string // "enter" | "exit" | "hold"

	PositionValue decimal.Decimal
	PositionPnl   decimal.Decimal
}
