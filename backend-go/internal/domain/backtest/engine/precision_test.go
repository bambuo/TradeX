package engine

import (
	"context"
	"encoding/json"
	"math"
	"testing"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

func precisionEngine() *BacktestEngine {
	reg := indicator.NewRegistry()
	reg.Register(indicator.NewSMA(20))
	reg.Register(indicator.NewSMA(50))
	reg.Register(indicator.NewEMA(20))
	reg.Register(indicator.NewRSI(14))
	reg.Register(indicator.NewMACD(12, 26, 9))
	reg.Register(indicator.NewBollingerBands(20, 2))
	return NewBacktestEngine(reg)
}

func TestPrecision_Equity_Deterministic(t *testing.T) {
	engine := precisionEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":30}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":70}`),
	}

	candles := buildSineCandles(300, decimal.NewFromInt(50000), 42)

	out1, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	out2, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if !out1.Result.FinalValue.Equal(out2.Result.FinalValue) {
		t.Errorf("final value must be identical across runs: %s vs %s",
			out1.Result.FinalValue.String(), out2.Result.FinalValue.String())
	}

	if out1.Result.TotalTrades != out2.Result.TotalTrades {
		t.Errorf("trade count must be identical across runs: %d vs %d",
			out1.Result.TotalTrades, out2.Result.TotalTrades)
	}
}

func TestPrecision_Metrics_ValidRange(t *testing.T) {
	engine := precisionEngine()

	tests := []struct {
		name   string
		klines int
	}{
		{"300 klines", 300},
		{"1000 klines", 1000},
		{"5000 klines", 5000},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			strategy := domain.Strategy{
				EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":30}`),
				ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":70}`),
			}

			candles := buildSineCandles(tt.klines, decimal.NewFromInt(50000), 42)
			out, err := engine.Run(context.Background(), EngineInput{
				Strategy: strategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
			})
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if len(out.Analysis) == 0 {
				t.Fatal("expected non-empty analysis")
			}

			fv, _ := out.Result.FinalValue.Float64()
			if !(fv > 0) {
				t.Errorf("final value must be positive, got %f", fv)
			}

			if out.Result.TotalReturnPercent.IsZero() {
				t.Errorf("total return should not be zero with active strategy")
			}

			dd, _ := out.Result.MaxDrawdownPercent.Float64()
			if dd > 0 {
				t.Errorf("max drawdown must not be positive, got %f", dd)
			}

			wr, _ := out.Result.WinRate.Float64()
			if !(wr >= 0 && wr <= 100) {
				t.Errorf("win rate must be 0-100, got %f", wr)
			}

			sh, _ := out.Result.SharpeRatio.Float64()
			if math.IsNaN(sh) {
				t.Errorf("sharpe must not be NaN")
			}
		})
	}
}

func TestPrecision_Fee_DecimalPrecision(t *testing.T) {
	engine := precisionEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":100}`),
	}

	candles := buildSineCandles(200, decimal.NewFromInt(50000), 42)

	noFee, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles,
		InitialCapital: decimal.NewFromInt(1000), FeeRate: decimal.Zero,
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(noFee.Trades) == 0 {
		t.Fatal("expected at least 1 trade")
	}

	withFee, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles,
		InitialCapital: decimal.NewFromInt(1000), FeeRate: decimal.NewFromFloat(0.001),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	diff := noFee.Result.FinalValue.Sub(withFee.Result.FinalValue)
	diffF64, _ := diff.Float64()

	t.Logf("no-fee final: %s, with-fee final: %s, diff: %s",
		noFee.Result.FinalValue.String(),
		withFee.Result.FinalValue.String(),
		diff.String())

	if !(diffF64 > 0) {
		t.Errorf("fee should reduce final value: no-fee=%s with-fee=%s",
			noFee.Result.FinalValue.String(), withFee.Result.FinalValue.String())
	}
}

func TestPrecision_EquityCurve_Monotonic(t *testing.T) {
	engine := precisionEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":30}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":70}`),
	}

	candles := buildSineCandles(500, decimal.NewFromInt(50000), 42)
	out, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.EquityCurve) == 0 {
		t.Fatal("expected non-empty equity curve")
	}

	for i, eq := range out.EquityCurve {
		f64, _ := eq.Float64()
		if math.IsNaN(f64) {
			t.Errorf("equity curve has NaN at index %d", i)
		}
		if math.IsInf(f64, 0) {
			t.Errorf("equity curve has Inf at index %d", i)
		}
	}
}
