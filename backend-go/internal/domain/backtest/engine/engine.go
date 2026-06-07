package engine

import (
	"context"
	"fmt"
	"math"
	"time"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	bt "tradex/internal/domain/backtest"
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
	OnAnalysis     func(bt.BacktestKlineAnalysis)
}

type EngineOutput struct {
	Result      bt.BacktestResult
	Trades      []bt.BacktestTrade
	Analysis    []bt.BacktestKlineAnalysis
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
	if len(input.Klines) < FirstValidIndex+1 {
		return EngineOutput{
			Result: bt.BacktestResult{
				InitialCapital: input.InitialCapital,
				FinalValue:     input.InitialCapital,
				TotalTrades:    0,
			},
			Trades:   []bt.BacktestTrade{},
			Analysis: []bt.BacktestKlineAnalysis{},
		}, nil
	}
	if err := e.validate(input); err != nil {
		return EngineOutput{}, err
	}

	trades := make([]bt.BacktestTrade, 0, 100)
	analysis := make([]bt.BacktestKlineAnalysis, 0, len(input.Klines))
	equityCurve := make([]decimal.Decimal, 0, len(input.Klines))

	cash := input.InitialCapital
	var inPosition bool
	var positionSize decimal.Decimal
	var avgEntryPrice decimal.Decimal
	var positionCount int
	var entryTimestamp time.Time
	var entryFee decimal.Decimal

	e.registry.ComputeAll(input.Klines)

	for i := FirstValidIndex; i < len(input.Klines); i++ {
		select {
		case <-ctx.Done():
			return EngineOutput{}, ctx.Err()
		default:
		}

		candle := input.Klines[i]
		closePrice := candle.Close

		currentEquity := cash
		if inPosition {
			currentEquity = currentEquity.Add(positionSize.Mul(closePrice))
		}

		decision := e.decision.Decide(DecisionInput{
			Strategy:      input.Strategy,
			Klines:        input.Klines,
			Index:         i,
			Registry:      e.registry,
			InPosition:    inPosition,
			CurrentEquity: currentEquity,
			CurrentPrice:  closePrice,
			PositionCount: positionCount,
			AvgEntryPrice: avgEntryPriceOrNil(inPosition, avgEntryPrice),
		})

		action := string(decision.Type)

		if !inPosition && decision.Type == DecisionEnter {
			// 入场
			var capitalToUse decimal.Decimal
			if decision.PositionSize != nil {
				capitalToUse = minDecimal(*decision.PositionSize, cash)
			} else if input.PositionSize != nil {
				capitalToUse = minDecimal(*input.PositionSize, cash)
			} else {
				capitalToUse = cash
			}

			entryQuantity := capitalToUse.Div(closePrice.Mul(decimal.NewFromFloat(1).Add(input.FeeRate)))
			calculatedEntryFee := entryQuantity.Mul(closePrice).Mul(input.FeeRate)
			cash = cash.Sub(entryQuantity.Mul(closePrice)).Sub(calculatedEntryFee)

			if !inPosition {
				positionSize = entryQuantity
				avgEntryPrice = closePrice
				positionCount = 1
				entryFee = calculatedEntryFee
			} else {
				totalQty := positionSize.Add(entryQuantity)
				if totalQty.IsPositive() {
					avgEntryPrice = positionSize.Mul(avgEntryPrice).Add(entryQuantity.Mul(closePrice)).Div(totalQty)
					entryFee = entryFee.Add(calculatedEntryFee)
				}
				positionSize = totalQty
				positionCount++
			}

			entryTimestamp = candle.Timestamp
			inPosition = true
		} else if inPosition && (decision.Type == DecisionExit || i == len(input.Klines)-1) {
			// 出场（含最后 K 线强制平仓）
			exitFee := positionSize.Mul(closePrice).Mul(input.FeeRate)
			pnl := positionSize.Mul(closePrice.Sub(avgEntryPrice)).Sub(entryFee).Sub(exitFee)
			costBasis := avgEntryPrice.Mul(positionSize).Add(entryFee)

			var pnlPercent decimal.Decimal
			if costBasis.IsPositive() {
				pnlPercent = pnl.Div(costBasis).Mul(decimal.NewFromInt(100))
			}

			cash = cash.Add(positionSize.Mul(closePrice)).Sub(exitFee)

			trade := bt.BacktestTrade{
				EntryIndex: 0,
				ExitIndex:  i,
				EnteredAt:  entryTimestamp,
				ExitedAt:   candle.Timestamp,
				EntryPrice: avgEntryPrice,
				ExitPrice:  closePrice,
				Quantity:   positionSize,
				PnL:        pnl,
				PnLPercent: pnlPercent,
			}
			if len(trades) > 0 {
				prev := trades[len(trades)-1]
				trade.EntryIndex = prev.ExitIndex + 1
			}
			trades = append(trades, trade)

			inPosition = false
			positionSize = decimal.Zero
			positionCount = 0
			entryFee = decimal.Zero
		}

		// 账户权益 = 现金 + 持仓市值
		equity := cash
		if inPosition {
			equity = equity.Add(positionSize.Mul(closePrice))
		}
		equityCurve = append(equityCurve, equity)

		// 构建分析记录
		var entryCondResult, exitCondResult *bool
		if !inPosition && decision.Type == DecisionEnter {
			entryCondResult = decision.ConditionResult
		} else if inPosition {
			exitCondResult = decision.ConditionResult
		} else {
			entryCondResult = decision.ConditionResult
		}

		analysisEntry := bt.BacktestKlineAnalysis{
			KlineIndex:           i,
			Timestamp:            candle.Timestamp,
			Open:                 candle.Open,
			High:                 candle.High,
			Low:                  candle.Low,
			Close:                candle.Close,
			Volume:               candle.Volume,
			EntryConditionResult: entryCondResult,
			ExitConditionResult:  exitCondResult,
			InPosition:           inPosition,
			Action:               action,
		}

		if inPosition {
			analysisEntry.AvgEntryPrice = new(avgEntryPrice)
			analysisEntry.PositionQuantity = &positionSize
			cost := avgEntryPrice.Mul(positionSize)
			analysisEntry.PositionCost = &cost
			value := positionSize.Mul(closePrice)
			analysisEntry.PositionValue = &value
			analysisEntry.PositionPnl = new(positionSize.Mul(closePrice.Sub(avgEntryPrice)))
			pnlPct := closePrice.Sub(avgEntryPrice).Div(avgEntryPrice).Mul(decimal.NewFromInt(100))
			analysisEntry.PositionPnlPercent = &pnlPct
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

func minDecimal(a, b decimal.Decimal) decimal.Decimal {
	if a.LessThan(b) {
		return a
	}
	return b
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

func (e *BacktestEngine) computeResult(input EngineInput, trades []bt.BacktestTrade, equityCurve []decimal.Decimal) bt.BacktestResult {
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
	sharpe := e.computeSharpeRatio(equityCurve, input.Timeframe)
	profitLossRatio := e.computeProfitLossRatio(trades)

	return bt.BacktestResult{
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

func (e *BacktestEngine) computeWinRate(trades []bt.BacktestTrade) decimal.Decimal {
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

func (e *BacktestEngine) computeSharpeRatio(equityCurve []decimal.Decimal, timeframe string) decimal.Decimal {
	if len(equityCurve) < 3 {
		return decimal.Zero
	}

	returns := make([]float64, 0, len(equityCurve)-1)
	for i := 1; i < len(equityCurve); i++ {
		prev, _ := equityCurve[i-1].Float64()
		if prev <= 0 {
			continue
		}
		cur, _ := equityCurve[i].Float64()
		returns = append(returns, cur/prev-1)
	}
	if len(returns) < 2 {
		return decimal.Zero
	}

	var sum float64
	for _, r := range returns {
		sum += r
	}
	mean := sum / float64(len(returns))

	var sumSq float64
	for _, r := range returns {
		d := r - mean
		sumSq += d * d
	}
	variance := sumSq / float64(len(returns)-1)
	std := math.Sqrt(math.Max(0, variance))
	if std <= 0 {
		return decimal.Zero
	}

	periods := periodsPerYear(timeframe)
	sharpe := mean / std * math.Sqrt(periods)
	sharpe = math.Max(-9999, math.Min(9999, sharpe))
	return decimal.NewFromFloat(sharpe)
}

func periodsPerYear(timeframe string) float64 {
	switch timeframe {
	case "1m":
		return 525_600
	case "5m":
		return 105_120
	case "15m":
		return 35_040
	case "30m":
		return 17_520
	case "1h":
		return 8_760
	case "4h":
		return 2_190
	case "1d":
		return 365
	default:
		return 365
	}
}

func (e *BacktestEngine) computeProfitLossRatio(trades []bt.BacktestTrade) decimal.Decimal {
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
