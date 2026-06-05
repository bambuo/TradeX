package domain

import (
	"github.com/shopspring/decimal"
)

type BacktestTrade struct {
	EntryIndex int
	ExitIndex  int

	EntryPrice decimal.Decimal
	ExitPrice  decimal.Decimal
	Quantity   decimal.Decimal

	PnL        decimal.Decimal
	PnLPercent decimal.Decimal

	EntryFee decimal.Decimal
	ExitFee  decimal.Decimal
}

type BacktestResult struct {
	FinalValue              decimal.Decimal
	TotalReturnPercent      decimal.Decimal
	AnnualizedReturnPercent decimal.Decimal
	MaxDrawdownPercent      decimal.Decimal
	WinRate                 decimal.Decimal
	SharpeRatio             decimal.Decimal
	ProfitLossRatio         decimal.Decimal
	TotalTrades             int
}
