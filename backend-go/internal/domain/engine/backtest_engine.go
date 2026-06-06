package engine

import (
	"context"
	"fmt"
	"math"
	"time"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

const (
	MaxKlines       = 100000
	FirstValidIndex = 50
)

type EngineInput struct {
	Strategy       domain.Strategy
	Pair           string
	Klines         []domain.Candle
	InitialCapital decimal.Decimal
	PositionSize   *decimal.Decimal
	FeeRate        decimal.Decimal
	Timeframe      string
	OnAnalysis     func(domain.BacktestKlineAnalysis)
}

type EngineOutput struct {
	Result      domain.BacktestResult
	Trades      []domain.BacktestTrade
	Analysis    []domain.BacktestKlineAnalysis
	EquityCurve []decimal.Decimal
}

type BacktestEngine struct {
	registry *indicator.Registry
	decision *StrategyDecisionEngine
}

func NewBacktestEngine(registry *indicator.Registry) *BacktestEngine {
	return &BacktestEngine{
		registry: registry,
		decision: NewStrategyDecisionEngine(),
	}
}

func (e *BacktestEngine) Run(ctx context.Context, input EngineInput) (EngineOutput, error) {
	if err := e.validate(input); err != nil {
		return EngineOutput{}, err
	}

	trades := make([]domain.BacktestTrade, 0, 100)
	analysis := make([]domain.BacktestKlineAnalysis, 0, len(input.Klines))
	equityCurve := make([]decimal.Decimal, len(input.Klines))

	capital := input.InitialCapital
	equity := input.InitialCapital
	var inPosition bool
	var positionSize decimal.Decimal
	var avgEntryPrice decimal.Decimal
	var positionCount int
	var entryTimestamp time.Time

	e.registry.ComputeAll(input.Klines)

	for i := FirstValidIndex; i < len(input.Klines); i++ {
		select {
		case <-ctx.Done():
			return EngineOutput{}, ctx.Err()
		default:
		}

		candle := input.Klines[i]
		closePrice := candle.Close

		decision := e.decision.Decide(DecisionInput{
			Strategy:      input.Strategy,
			Klines:        input.Klines,
			Index:         i,
			Registry:      e.registry,
			InPosition:    inPosition,
			CurrentEquity: equity,
			CurrentPrice:  closePrice,
			PositionCount: positionCount,
			AvgEntryPrice: avgEntryPriceOrNil(inPosition, avgEntryPrice),
		})

		switch decision.Type {
		case DecisionEnter:
			posSize := input.PositionSize
			if decision.PositionSize != nil {
				posSize = decision.PositionSize
			}

			amount := posSize
			if amount == nil {
				amount = new(equity)
			}

			if amount.GreaterThan(equity) {
				amount = new(equity)
			}

			qty := amount.Div(closePrice)
			fee := amount.Mul(input.FeeRate)
			capital = capital.Sub(*amount).Sub(fee)

			if !inPosition {
				positionSize = qty
				avgEntryPrice = closePrice
				positionCount = 1
			} else {
				totalQty := positionSize.Add(qty)
				if totalQty.IsPositive() {
					avgEntryPrice = positionSize.Mul(avgEntryPrice).Add(qty.Mul(closePrice)).Div(totalQty)
				}
				positionSize = totalQty
				positionCount++
			}

			entryTimestamp = candle.Timestamp
			inPosition = true

		case DecisionExit:
			if inPosition {
				sellValue := positionSize.Mul(closePrice)
				fee := sellValue.Mul(input.FeeRate)
				pnl := positionSize.Mul(closePrice.Sub(avgEntryPrice))

				totalFee := fee
				capital = capital.Add(positionSize.Mul(closePrice)).Sub(totalFee)

				trade := domain.BacktestTrade{
					EntryIndex: 0,
					ExitIndex:  i,
					EnteredAt:  entryTimestamp,
					ExitedAt:   candle.Timestamp,
					EntryPrice: avgEntryPrice,
					ExitPrice:  closePrice,
					Quantity:   positionSize,
					PnL:        pnl,
					PnLPercent: pnl.Div(avgEntryPrice.Mul(positionSize)).Mul(decimal.NewFromInt(100)),
				}
				if len(trades) > 0 {
					prev := trades[len(trades)-1]
					trade.EntryIndex = prev.ExitIndex + 1
				}
				trades = append(trades, trade)

				equity = capital
				inPosition = false
				positionSize = decimal.Zero
				positionCount = 0
			}

		case DecisionHold:
			if inPosition {
				equity = capital.Add(positionSize.Mul(closePrice))
			} else {
				equity = capital
			}
		}

		equityCurve[i] = equity

		analysisEntry := domain.BacktestKlineAnalysis{
			KlineIndex: i,
			Timestamp:  candle.Timestamp,
			Open:       candle.Open,
			High:       candle.High,
			Low:        candle.Low,
			Close:      candle.Close,
			Volume:     candle.Volume,
			InPosition: inPosition,
			Action:     string(decision.Type),
		}
		if inPosition {
			analysisEntry.AvgEntryPrice = new(avgEntryPrice)
			analysisEntry.PositionQuantity = &positionSize
			analysisEntry.PositionValue = new(positionSize.Mul(closePrice))
			analysisEntry.PositionPnl = new(positionSize.Mul(closePrice.Sub(avgEntryPrice)))
		}

		indicatorValues := make(map[string]float64)
		for _, name := range e.registry.Indicators() {
			if val, ok := e.registry.GetValue(name, i); ok {
				indicatorValues[name] = val
			}
		}
		analysisEntry.IndicatorValues = indicatorValues

		analysis = append(analysis, analysisEntry)

		if input.OnAnalysis != nil {
			input.OnAnalysis(analysisEntry)
		}
	}

	result := e.computeResult(input, trades, equityCurve)
	return EngineOutput{
		Result:      result,
		Trades:      trades,
		Analysis:    analysis,
		EquityCurve: equityCurve,
	}, nil
}

func (e *BacktestEngine) validate(input EngineInput) error {
	if len(input.Klines) < FirstValidIndex+1 {
		return fmt.Errorf("not enough klines: got %d, need at least %d", len(input.Klines), FirstValidIndex+1)
	}
	if len(input.Klines) > MaxKlines {
		return fmt.Errorf("too many klines: got %d, max %d", len(input.Klines), MaxKlines)
	}
	if input.InitialCapital.IsNegative() || input.InitialCapital.IsZero() {
		return fmt.Errorf("initial capital must be positive")
	}
	return nil
}

func (e *BacktestEngine) computeResult(input EngineInput, trades []domain.BacktestTrade, equityCurve []decimal.Decimal) domain.BacktestResult {
	initialCapital := input.InitialCapital
	finalEquity := equityCurve[len(equityCurve)-1]

	totalReturn := decimal.Zero
	if initialCapital.IsPositive() {
		totalReturn = finalEquity.Sub(initialCapital).Div(initialCapital).Mul(decimal.NewFromInt(100))
	}

	duration := input.Klines[len(input.Klines)-1].Timestamp.Sub(input.Klines[FirstValidIndex].Timestamp)
	days := duration.Hours() / 24
	if days < 1 {
		days = 1
	}

	annualizedReturn := decimal.Zero
	if initialCapital.IsPositive() && !finalEquity.IsNegative() {
		ratio := finalEquity.Div(initialCapital)
		if ratio.IsPositive() {
			power := 365.0 / days
			ratioF64, _ := ratio.Float64()
			ratioF64 = math.Max(ratioF64, 0.0001)
			ratioF64 = math.Min(ratioF64, 1e10)
			annF64 := math.Pow(ratioF64, power) - 1
			if math.IsInf(annF64, 0) || math.IsNaN(annF64) {
				annF64 = 9999
			}
			annF64 = math.Max(annF64, -99.99)
			annF64 = math.Min(annF64, 9999)
			annualizedReturn = decimal.NewFromFloat(annF64).Mul(decimal.NewFromInt(100))
		}
	}

	maxDrawdown := e.computeMaxDrawdown(equityCurve)
	winRate := e.computeWinRate(trades)
	sharpe := e.computeSharpeRatio(equityCurve, days)
	profitLossRatio := e.computeProfitLossRatio(trades)

	return domain.BacktestResult{
		FinalValue:              finalEquity,
		TotalReturnPercent:      totalReturn,
		AnnualizedReturnPercent: annualizedReturn,
		MaxDrawdownPercent:      maxDrawdown,
		WinRate:                 winRate,
		SharpeRatio:             sharpe,
		ProfitLossRatio:         profitLossRatio,
		TotalTrades:             len(trades),
	}
}

func (e *BacktestEngine) computeMaxDrawdown(equityCurve []decimal.Decimal) decimal.Decimal {
	var peak decimal.Decimal
	var maxDD decimal.Decimal

	for _, eq := range equityCurve {
		if eq.IsZero() {
			continue
		}
		if eq.GreaterThan(peak) {
			peak = eq
		}
		if peak.IsPositive() {
			dd := eq.Sub(peak).Div(peak).Mul(decimal.NewFromInt(100))
			if dd.LessThan(maxDD) {
				maxDD = dd
			}
		}
	}

	return maxDD
}

func (e *BacktestEngine) computeWinRate(trades []domain.BacktestTrade) decimal.Decimal {
	if len(trades) == 0 {
		return decimal.Zero
	}
	wins := 0
	for _, t := range trades {
		if t.PnL.IsPositive() {
			wins++
		}
	}
	return decimal.NewFromInt(int64(wins)).Div(decimal.NewFromInt(int64(len(trades)))).Mul(decimal.NewFromInt(100))
}

func (e *BacktestEngine) computeSharpeRatio(equityCurve []decimal.Decimal, days float64) decimal.Decimal {
	returns := make([]float64, 0, len(equityCurve))
	prev := equityCurve[FirstValidIndex]
	for i := FirstValidIndex + 1; i < len(equityCurve); i++ {
		if prev.IsPositive() {
			ret, _ := equityCurve[i].Sub(prev).Div(prev).Float64()
			returns = append(returns, ret)
		}
		prev = equityCurve[i]
	}

	if len(returns) < 2 {
		return decimal.Zero
	}

	var sum, sumSq float64
	for _, r := range returns {
		sum += r
	}
	mean := sum / float64(len(returns))

	for _, r := range returns {
		d := r - mean
		sumSq += d * d
	}
	variance := sumSq / float64(len(returns)-1)
	std := math.Sqrt(variance)

	if std < 1e-15 {
		return decimal.Zero
	}

	periodsPerYear := 365.0 / days * float64(len(returns))
	sharpe := (mean / std) * math.Sqrt(periodsPerYear)

	return decimal.NewFromFloat(sharpe)
}

func (e *BacktestEngine) computeProfitLossRatio(trades []domain.BacktestTrade) decimal.Decimal {
	totalProfit := decimal.Zero
	totalLoss := decimal.Zero

	for _, t := range trades {
		if t.PnL.IsPositive() {
			totalProfit = totalProfit.Add(t.PnL)
		} else {
			totalLoss = totalLoss.Add(t.PnL.Abs())
		}
	}

	if totalLoss.IsZero() {
		if totalProfit.IsZero() {
			return decimal.Zero
		}
		return decimal.NewFromInt(999)
	}

	return totalProfit.Div(totalLoss)
}

func avgEntryPriceOrNil(inPosition bool, price decimal.Decimal) *decimal.Decimal {
	if inPosition {
		return &price
	}
	return nil
}
