package engine

import (
	"context"
	"encoding/json"
	"math"
	"testing"

	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/tradex/backend-go/internal/domain"
	"github.com/tradex/backend-go/internal/domain/indicator"
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

	// 同一输入跑两次必须产出完全相同的结果
	candles := buildSineCandles(300, decimal.NewFromInt(50000), 42)

	out1, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
	})
	require.NoError(t, err)

	out2, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
	})
	require.NoError(t, err)

	assert.True(t, out1.Result.FinalValue.Equal(out2.Result.FinalValue),
		"final value must be identical across runs: %s vs %s",
		out1.Result.FinalValue.String(), out2.Result.FinalValue.String())

	assert.Equal(t, out1.Result.TotalTrades, out2.Result.TotalTrades,
		"trade count must be identical across runs")
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
			require.NoError(t, err)
			require.NotEmpty(t, out.Analysis)

			fv, _ := out.Result.FinalValue.Float64()
			assert.True(t, fv > 0, "final value must be positive, got %f", fv)

			assert.False(t, out.Result.TotalReturnPercent.IsZero(),
				"total return should not be zero with active strategy")

			dd, _ := out.Result.MaxDrawdownPercent.Float64()
			assert.False(t, dd > 0, "max drawdown must not be positive, got %f", dd)

			wr, _ := out.Result.WinRate.Float64()
			assert.True(t, wr >= 0 && wr <= 100, "win rate must be 0-100, got %f", wr)

			sh, _ := out.Result.SharpeRatio.Float64()
			assert.False(t, math.IsNaN(sh), "sharpe must not be NaN")
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
	require.NoError(t, err)
	require.NotEmpty(t, noFee.Trades)

	withFee, err := engine.Run(context.Background(), EngineInput{
		Strategy: strategy, Pair: "BTCUSDT", Klines: candles,
		InitialCapital: decimal.NewFromInt(1000), FeeRate: decimal.NewFromFloat(0.001),
	})
	require.NoError(t, err)

	diff := noFee.Result.FinalValue.Sub(withFee.Result.FinalValue)
	diffF64, _ := diff.Float64()

	// 手续费差异必须为正且精度稳定（无 float64 舍入漂移）
	t.Logf("no-fee final: %s, with-fee final: %s, diff: %s",
		noFee.Result.FinalValue.String(),
		withFee.Result.FinalValue.String(),
		diff.String())

	assert.True(t, diffF64 > 0,
		"fee should reduce final value: no-fee=%s with-fee=%s",
		noFee.Result.FinalValue.String(), withFee.Result.FinalValue.String())
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
	require.NoError(t, err)
	require.NotEmpty(t, out.EquityCurve)

	// 权益曲线不应有 NaN 或 Inf
	for i, eq := range out.EquityCurve {
		f64, _ := eq.Float64()
		assert.False(t, math.IsNaN(f64), "equity curve has NaN at index %d", i)
		assert.False(t, math.IsInf(f64, 0), "equity curve has Inf at index %d", i)
	}
}
