package backtest

import (
	"time"

	"github.com/shopspring/decimal"
)

type BacktestKlineAnalysis struct {
	Index     int             `json:"index"`
	Timestamp time.Time       `json:"timestamp"`
	Open      decimal.Decimal `json:"open"`
	High      decimal.Decimal `json:"high"`
	Low       decimal.Decimal `json:"low"`
	Close     decimal.Decimal `json:"close"`
	Volume    decimal.Decimal `json:"volume"`

	IndicatorValues      map[string]float64 `json:"indicators,omitempty"`
	EntryConditionResult *bool              `json:"entry,omitempty"`
	ExitConditionResult  *bool              `json:"exit,omitempty"`

	InPosition         bool             `json:"inPosition"`
	Action             string           `json:"action"`
	AvgEntryPrice      *decimal.Decimal `json:"avgEntryPrice,omitempty"`
	PositionQuantity   *decimal.Decimal `json:"positionQuantity,omitempty"`
	PositionCost       *decimal.Decimal `json:"positionCost,omitempty"`
	PositionValue      *decimal.Decimal `json:"positionValue,omitempty"`
	PositionPnl        *decimal.Decimal `json:"positionPnl,omitempty"`
	PositionPnlPercent *decimal.Decimal `json:"positionPnlPercent,omitempty"`
}

func NewBacktestKlineAnalysis(index int, ts time.Time, open, high, low, close_, volume decimal.Decimal) BacktestKlineAnalysis {
	return BacktestKlineAnalysis{
		Index:     index,
		Timestamp: ts,
		Open:      open,
		High:      high,
		Low:       low,
		Close:     close_,
		Volume:    volume,
	}
}
