package backtest

import (
	"time"

	"github.com/shopspring/decimal"
)

type BacktestTrade struct {
	EntryIndex int             `json:"entryIndex"`
	ExitIndex  int             `json:"exitIndex"`
	EnteredAt  time.Time       `json:"enteredAt"`
	ExitedAt   time.Time       `json:"exitedAt"`
	EntryPrice decimal.Decimal `json:"entryPrice"`
	ExitPrice  decimal.Decimal `json:"exitPrice"`
	Quantity   decimal.Decimal `json:"quantity"`
	Pnl        decimal.Decimal `json:"pnl"`
	PnlPercent decimal.Decimal `json:"pnlPercent"`
}

func (t BacktestTrade) Equal(other BacktestTrade) bool {
	return t.EntryIndex == other.EntryIndex &&
		t.ExitIndex == other.ExitIndex &&
		t.EnteredAt.Equal(other.EnteredAt) &&
		t.ExitedAt.Equal(other.ExitedAt) &&
		t.EntryPrice.Equal(other.EntryPrice) &&
		t.ExitPrice.Equal(other.ExitPrice) &&
		t.Quantity.Equal(other.Quantity) &&
		t.Pnl.Equal(other.Pnl) &&
		t.PnlPercent.Equal(other.PnlPercent)
}

type BacktestResult struct {
	StrategyName            string          `json:"strategyName"`
	Pair                    string          `json:"pair"`
	Timeframe               string          `json:"timeframe"`
	StartAt                 time.Time       `json:"startAt"`
	EndAt                   time.Time       `json:"endAt"`
	InitialCapital          decimal.Decimal `json:"initialCapital"`
	FinalValue              decimal.Decimal `json:"finalValue"`
	TotalReturnPercent      decimal.Decimal `json:"totalReturnPercent"`
	AnnualizedReturnPercent decimal.Decimal `json:"annualizedReturnPercent"`
	MaxDrawdownPercent      decimal.Decimal `json:"maxDrawdownPercent"`
	WinRate                 decimal.Decimal `json:"winRate"`
	SharpeRatio             decimal.Decimal `json:"sharpeRatio"`
	ProfitLossRatio         decimal.Decimal `json:"profitLossRatio"`
	TotalTrades             int             `json:"totalTrades"`
}

func (r *BacktestResult) FillMeta(strategyName, pair, timeframe string, startAt, endAt time.Time, initialCapital decimal.Decimal) {
	r.StrategyName = strategyName
	r.Pair = pair
	r.Timeframe = timeframe
	r.StartAt = startAt
	r.EndAt = endAt
	r.InitialCapital = initialCapital
}

func (r BacktestResult) Equal(other BacktestResult) bool {
	return r.StrategyName == other.StrategyName &&
		r.Pair == other.Pair &&
		r.Timeframe == other.Timeframe &&
		r.StartAt.Equal(other.StartAt) &&
		r.EndAt.Equal(other.EndAt) &&
		r.InitialCapital.Equal(other.InitialCapital) &&
		r.FinalValue.Equal(other.FinalValue) &&
		r.TotalReturnPercent.Equal(other.TotalReturnPercent) &&
		r.AnnualizedReturnPercent.Equal(other.AnnualizedReturnPercent) &&
		r.MaxDrawdownPercent.Equal(other.MaxDrawdownPercent) &&
		r.WinRate.Equal(other.WinRate) &&
		r.SharpeRatio.Equal(other.SharpeRatio) &&
		r.ProfitLossRatio.Equal(other.ProfitLossRatio) &&
		r.TotalTrades == other.TotalTrades
}
