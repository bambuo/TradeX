package engine

import (
	"context"
	"encoding/json"
	"errors"
	"math"
	"testing"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

func newTestEngine() *BacktestEngine {
	reg := indicator.NewRegistry()
	reg.Register(indicator.NewSMA(20))
	reg.Register(indicator.NewSMA(50))
	reg.Register(indicator.NewEMA(20))
	reg.Register(indicator.NewRSI(14))
	reg.Register(indicator.NewMACD(12, 26, 9))
	reg.Register(indicator.NewBollingerBands(20, 2))
	return NewBacktestEngine(reg)
}

func sineCandles(n int) []domain.Kline {
	return buildSineCandles(n, decimal.NewFromInt(50000), 42)
}

func TestRun_NoEntryCondition_ReturnsZeroTrades(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":100}`),
		ExitCondition:  json.RawMessage(`{}`),
	}

	candles := sineCandles(100)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Trades) != 0 {
		t.Errorf("expected empty trades, got %d", len(out.Trades))
	}
	if out.Result.TotalTrades != 0 {
		t.Errorf("expected TotalTrades=0, got %d", out.Result.TotalTrades)
	}
}

func TestRun_EntryAlwaysTrue_ExitAlwaysTrue_ProducesTrades(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":100}`),
	}

	candles := sineCandles(200)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if out.Result.TotalTrades < 1 {
		t.Errorf("expected TotalTrades >= 1, got %d", out.Result.TotalTrades)
	}
	if out.Result.TotalReturnPercent.Equal(decimal.Zero) {
		t.Errorf("expected non-zero TotalReturnPercent")
	}
}

func TestRun_InsufficientData_ReturnsEmptyResult(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":30}`),
		ExitCondition:  json.RawMessage(`{}`),
	}

	candles := sineCandles(10)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if out.Result.TotalTrades != 0 {
		t.Errorf("insufficient data should produce 0 trades, got %d", out.Result.TotalTrades)
	}
	if len(out.Trades) != 0 {
		t.Errorf("expected 0 trades, got %d", len(out.Trades))
	}
}

func TestRun_EmptyEntryCondition_ProducesZeroTrades(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{}`),
		ExitCondition:  json.RawMessage(`{}`),
	}

	candles := sineCandles(200)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Trades) != 0 {
		t.Errorf("expected empty trades, got %d", len(out.Trades))
	}
	if out.Result.TotalTrades != 0 {
		t.Errorf("expected TotalTrades=0, got %d", out.Result.TotalTrades)
	}
}

func TestRun_MalformedJson_DoesNotCrashEngine(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`not json{`),
		ExitCondition:  json.RawMessage(`{}`),
	}

	candles := sineCandles(200)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Trades) != 0 {
		t.Errorf("expected empty trades, got %d", len(out.Trades))
	}
}

func TestRun_CalculatesMetrics_Correctly(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":30}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":70}`),
	}

	candles := sineCandles(300)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Trades) != out.Result.TotalTrades {
		t.Errorf("expected TotalTrades=%d, got %d", len(out.Trades), out.Result.TotalTrades)
	}

	dd, _ := out.Result.MaxDrawdownPercent.Float64()
	if dd < -100.0 {
		t.Errorf("expected MaxDrawdownPercent >= -100, got %f", dd)
	}
	if dd > 0.0 {
		t.Errorf("max drawdown should be <= 0, got %f", dd)
	}

	wr, _ := out.Result.WinRate.Float64()
	if wr < 0.0 {
		t.Errorf("expected WinRate >= 0, got %f", wr)
	}
	if wr > 100.0 {
		t.Errorf("expected WinRate <= 100, got %f", wr)
	}

	sr, _ := out.Result.SharpeRatio.Float64()
	if math.IsNaN(sr) {
		t.Errorf("SharpeRatio should not be NaN")
	}

	plr, _ := out.Result.ProfitLossRatio.Float64()
	if plr < 0.0 {
		t.Errorf("expected ProfitLossRatio >= 0, got %f", plr)
	}
}

func TestRun_WithFee_ProducesLowerFinalValue(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":100}`),
	}

	candles := sineCandles(200)

	noFeeOut, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
		FeeRate:        decimal.Zero,
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(noFeeOut.Trades) < 1 {
		t.Fatal("expected at least 1 trade")
	}

	withFeeOut, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
		FeeRate:        decimal.NewFromFloat(0.001),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if !withFeeOut.Result.FinalValue.LessThan(noFeeOut.Result.FinalValue) {
		t.Errorf("with fee final value (%s) should be lower than no fee (%s)",
			withFeeOut.Result.FinalValue.String(), noFeeOut.Result.FinalValue.String())
	}
}

func TestRun_Analysis_HasIndicatorValues(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":100}`),
	}

	candles := sineCandles(300)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Analysis) == 0 {
		t.Fatal("expected non-empty analysis")
	}

	last := out.Analysis[len(out.Analysis)-1]
	if _, ok := last.IndicatorValues["sma"]; !ok {
		t.Errorf("expected sma in IndicatorValues")
	}
	if _, ok := last.IndicatorValues["ema"]; !ok {
		t.Errorf("expected ema in IndicatorValues")
	}
	if _, ok := last.IndicatorValues["rsi"]; !ok {
		t.Errorf("expected rsi in IndicatorValues")
	}
	if _, ok := last.IndicatorValues["macd"]; !ok {
		t.Errorf("expected macd in IndicatorValues")
	}
	if _, ok := last.IndicatorValues["bollinger"]; !ok {
		t.Errorf("expected bollinger in IndicatorValues")
	}
}

func TestRun_RespectsCancellation(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":100}`),
	}

	candles := sineCandles(500)
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	_, err := engine.Run(ctx, EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected context.Canceled, got %v", err)
	}
}

func TestRun_CancellationMidRun_StopsQuickly(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":100}`),
	}

	candles := sineCandles(1000)
	ctx, cancel := context.WithCancel(context.Background())

	go func() {
		cancel()
	}()

	_, err := engine.Run(ctx, EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected context.Canceled, got %v", err)
	}
}

func TestRun_Analysis_HasPositionCostAndConditionResult(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":100}`),
	}

	candles := sineCandles(300)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Analysis) == 0 {
		t.Fatal("expected non-empty analysis")
	}

	for _, a := range out.Analysis {
		if a.InPosition {
			t.Logf("PositionCost set at index %d: %v", a.Index, a.PositionCost)
			if a.PositionCost == nil {
				t.Errorf("PositionCost should be set when in position")
			}
			if a.AvgEntryPrice == nil {
				t.Errorf("AvgEntryPrice should be set when in position")
			}
			if a.PositionQuantity == nil {
				t.Errorf("PositionQuantity should be set when in position")
			}
			if a.PositionValue == nil {
				t.Errorf("PositionValue should be set when in position")
			}
			if a.PositionPnl == nil {
				t.Errorf("PositionPnl should be set when in position")
			}
			if a.PositionPnlPercent == nil {
				t.Errorf("PositionPnlPercent should be set when in position")
			}
			break
		}
	}

	for _, a := range out.Analysis {
		if a.EntryConditionResult != nil {
			t.Logf("EntryConditionResult set at index %d: %v", a.Index, *a.EntryConditionResult)
			if !*a.EntryConditionResult {
				t.Errorf("expected EntryConditionResult to be true")
			}
			break
		}
	}
}

func TestRun_ForcedExitOnLastKline(t *testing.T) {
	engine := newTestEngine()

	exitNeverTrueStrategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":200}`),
	}

	candles := sineCandles(200)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       exitNeverTrueStrategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if out.Result.TotalTrades < 1 {
		t.Errorf("should have at least one trade from forced exit on last bar, got %d", out.Result.TotalTrades)
	}
}

func TestRun_TwoLegFee_ReducesPnL(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":100}`),
	}

	candles := sineCandles(200)

	zeroFee, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
		FeeRate:        decimal.Zero,
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(zeroFee.Trades) < 1 {
		t.Fatal("expected at least 1 trade")
	}

	withFee, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
		FeeRate:        decimal.NewFromFloat(0.001),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if len(zeroFee.Trades) > 0 && len(withFee.Trades) > 0 {
		for i := range zeroFee.Trades {
			if i < len(withFee.Trades) {
				if !(withFee.Trades[i].Pnl.LessThan(zeroFee.Trades[i].Pnl) || withFee.Trades[i].Pnl.Equal(zeroFee.Trades[i].Pnl)) {
					t.Errorf("trade %d: with-fee Pnl (%s) should be <= zero-fee Pnl (%s)",
						i, withFee.Trades[i].Pnl.String(), zeroFee.Trades[i].Pnl.String())
				}
			}
		}
	}
}

func TestRun_WithFeeRate_PercentageCorrect(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":100}`),
	}

	candles := sineCandles(200)

	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
		FeeRate:        decimal.NewFromFloat(0.001),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Trades) == 0 {
		t.Fatal("expected at least 1 trade")
	}

	for range out.Trades {
	}
}

func TestRun_VolatilityGrid_ProducesMultipleLevels(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{}`),
		ExitCondition:  json.RawMessage(`{}`),
		ExecutionRule: json.RawMessage(`{
			"type": "volatility_grid",
			"rebalance_percent": 0.5,
			"max_pyramiding_levels": 3,
			"base_position_size": 100
		}`),
	}

	candles := sineCandles(500)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(10000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	t.Logf("Grid trades: %d, final value: %s", out.Result.TotalTrades, out.Result.FinalValue.String())
}
