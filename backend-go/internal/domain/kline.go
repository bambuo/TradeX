package domain

import (
	"time"

	"github.com/shopspring/decimal"
)

type Kline struct {
	Timestamp time.Time       `json:"timestamp"`
	Open      decimal.Decimal `json:"open"`
	High      decimal.Decimal `json:"high"`
	Low       decimal.Decimal `json:"low"`
	Close     decimal.Decimal `json:"close"`
	Volume    decimal.Decimal `json:"volume"`
}

func (c Kline) Equal(other Kline) bool {
	return c.Timestamp.Equal(other.Timestamp) &&
		c.Open.Equal(other.Open) &&
		c.High.Equal(other.High) &&
		c.Low.Equal(other.Low) &&
		c.Close.Equal(other.Close) &&
		c.Volume.Equal(other.Volume)
}
