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
	EntryConditionResult *bool
	ExitConditionResult  *bool

	InPosition         bool
	Action             string // "enter" | "exit" | "hold"
	AvgEntryPrice      *decimal.Decimal
	PositionQuantity   *decimal.Decimal
	PositionCost       *decimal.Decimal
	PositionValue      *decimal.Decimal
	PositionPnl        *decimal.Decimal
	PositionPnlPercent *decimal.Decimal
}
