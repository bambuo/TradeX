package backtest

import (
	"time"

	"github.com/shopspring/decimal"
)

type BacktestTrade struct {
	EntryIndex int
	ExitIndex  int
	EnteredAt  time.Time
	ExitedAt   time.Time

	EntryPrice decimal.Decimal
	ExitPrice  decimal.Decimal
	Quantity   decimal.Decimal

	PnL        decimal.Decimal
	PnLPercent decimal.Decimal
}

type BacktestResult struct {
	StrategyName            string
	Pair                    string
	Timeframe               string
	StartAt                 time.Time
	EndAt                   time.Time
	InitialCapital          decimal.Decimal
	FinalValue              decimal.Decimal
	TotalReturnPercent      decimal.Decimal
	AnnualizedReturnPercent decimal.Decimal
	MaxDrawdownPercent      decimal.Decimal
	WinRate                 decimal.Decimal
	SharpeRatio             decimal.Decimal
	ProfitLossRatio         decimal.Decimal
	TotalTrades             int
}
