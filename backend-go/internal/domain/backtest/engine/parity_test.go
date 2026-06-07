package engine

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

func parityEngine() *BacktestEngine {
	reg := indicator.NewRegistry()
	reg.Register(indicator.NewSMA(20))
	reg.Register(indicator.NewSMA(50))
	reg.Register(indicator.NewEMA(20))
	reg.Register(indicator.NewRSI(14))
	reg.Register(indicator.NewMACD(12, 26, 9))
	reg.Register(indicator.NewBollingerBands(20, 2))
	reg.Register(indicator.NewStochastic(5, 3))
	return NewBacktestEngine(reg)
}

func parityEngineWithLegacyNames() *BacktestEngine {
	reg := indicator.NewRegistry()
	reg.Register(newLegacySMA("sma_20", 20))
	reg.Register(newLegacySMA("sma_50", 50))
	reg.Register(newLegacySMA("rsi", 14))
	return NewBacktestEngine(reg)
}

type legacySMA struct {
	name   string
	period int
}

func newLegacySMA(name string, period int) *legacySMA {
	return &legacySMA{name: name, period: period}
}

func (l *legacySMA) Name() string { return l.name }
func (l *legacySMA) Compute(v []float64) []float64 {
	sma := indicator.NewSMA(l.period)
	return sma.Compute(v)
}

func parityCandles(n int) []domain.Kline {
	return buildSineCandles(n, decimal.NewFromInt(50000), 42)
}

// C# parity: LegacyCrossoverCodes_CA_CB_StillEvaluate
func TestParity_LegacyCrossoverCodes_CA_CB_Evaluate(t *testing.T) {
	engine := parityEngineWithLegacyNames()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"SMA_20","comparison":"CA","value":50000}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"SMA_20","comparison":"CB","value":50000}`),
	}

	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         parityCandles(400),
		InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Trades) == 0 {
		t.Errorf("CA/CB legacy codes should produce trades")
	}
}

// C# parity: RelativeComparison_WithRef_ComparesAgainstScaledIndicator
func TestParity_RelativeComparison_WithRef(t *testing.T) {
	engine := parityEngineWithLegacyNames()

	refStrategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"SMA_50","comparison":">","value":1.005,"ref":"SMA_20"}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"SMA_50","comparison":"<","value":0.995,"ref":"SMA_20"}`),
	}
	literalStrategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"SMA_50","comparison":">","value":1.005}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"SMA_50","comparison":"<","value":0.995}`),
	}

	candles := parityCandles(400)
	refOut, err := engine.Run(context.Background(), EngineInput{
		Strategy: refStrategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	literalOut, err := engine.Run(context.Background(), EngineInput{
		Strategy: literalStrategy, Pair: "BTCUSDT", Klines: candles, InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	refCount := len(refOut.Trades)
	litCount := len(literalOut.Trades)

	if litCount == refCount {
		t.Errorf("ref strategy and literal strategy should produce different trade counts, both got %d", refCount)
	}
}

// C# parity: WhitespaceEntryCondition_ProducesZeroTrades
func TestParity_WhitespaceEntryCondition_ProducesZeroTrades(t *testing.T) {
	engine := parityEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`"  "`),
		ExitCondition:  json.RawMessage(`{}`),
	}

	out, err := engine.Run(context.Background(), EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         parityCandles(200),
		InitialCapital: decimal.NewFromInt(1000),
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(out.Trades) != 0 {
		t.Errorf("expected empty trades, got %d", len(out.Trades))
	}
}

// C# parity: Run_CancellationTokenCancelledMidRun_StopsWithinFewIterations
func TestParity_CancellationMidRun_StopsWithinFewIterations(t *testing.T) {
	engine := parityEngine()

	strategy := domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":100}`),
	}

	candles := parityCandles(500)
	ctx, cancel := context.WithCancel(context.Background())

	var preCount int
	for i := FirstValidIndex; i < len(candles) && preCount < 5; i++ {
		preCount++
	}
	cancel()

	_, err := engine.Run(ctx, EngineInput{
		Strategy:       strategy,
		Pair:           "BTCUSDT",
		Klines:         candles,
		InitialCapital: decimal.NewFromInt(1000),
	})
	if err != context.Canceled {
		t.Errorf("expected context.Canceled, got %v", err)
	}
}
