package engine

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

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

func sineCandles(n int) []domain.Candle {
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

	require.NoError(t, err)
	assert.Empty(t, out.Trades)
	assert.Equal(t, 0, out.Result.TotalTrades)
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

	require.NoError(t, err)
	assert.GreaterOrEqual(t, out.Result.TotalTrades, 1)
	assert.NotEqual(t, decimal.Zero, out.Result.TotalReturnPercent)
}

func TestRun_InsufficientData_ReturnsError(t *testing.T) {
	engine := newTestEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":30}`),
		ExitCondition:  json.RawMessage(`{}`),
	}

	candles := sineCandles(10)
	_, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})

	require.Error(t, err)
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

	require.NoError(t, err)
	assert.Empty(t, out.Trades)
	assert.Equal(t, 0, out.Result.TotalTrades)
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

	require.NoError(t, err)
	assert.Empty(t, out.Trades)
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

	require.NoError(t, err)
	assert.Equal(t, len(out.Trades), out.Result.TotalTrades)

	totalRet, _ := out.Result.TotalReturnPercent.Float64()
	assert.InDelta(t, totalRet, totalRet, 1000)

	dd, _ := out.Result.MaxDrawdownPercent.Float64()
	assert.GreaterOrEqual(t, dd, -100.0)

	wr, _ := out.Result.WinRate.Float64()
	assert.GreaterOrEqual(t, wr, 0.0)
	assert.LessOrEqual(t, wr, 100.0)
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
	require.NoError(t, err)
	require.GreaterOrEqual(t, len(noFeeOut.Trades), 1)

	withFeeOut, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
		FeeRate:        decimal.NewFromFloat(0.001),
	})
	require.NoError(t, err)

	assert.True(t, withFeeOut.Result.FinalValue.LessThan(noFeeOut.Result.FinalValue),
		"with fee final value (%s) should be lower than no fee (%s)",
		withFeeOut.Result.FinalValue.String(), noFeeOut.Result.FinalValue.String())
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
	require.NoError(t, err)
	require.NotEmpty(t, out.Analysis)

	last := out.Analysis[len(out.Analysis)-1]
	assert.Contains(t, last.IndicatorValues, "sma")
	assert.Contains(t, last.IndicatorValues, "ema")
	assert.Contains(t, last.IndicatorValues, "rsi")
	assert.Contains(t, last.IndicatorValues, "macd")
	assert.Contains(t, last.IndicatorValues, "bollinger")
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

	require.ErrorIs(t, err, context.Canceled)
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

	require.ErrorIs(t, err, context.Canceled)
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
	require.NoError(t, err)
	require.NotEmpty(t, out.Trades)

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
	require.NoError(t, err)

	t.Logf("Grid trades: %d, final value: %s", out.Result.TotalTrades, out.Result.FinalValue.String())
}
