package engine

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

func benchmarkEngine() *BacktestEngine {
	reg := indicator.NewRegistry()
	reg.Register(indicator.NewSMA(20))
	reg.Register(indicator.NewSMA(50))
	reg.Register(indicator.NewEMA(20))
	reg.Register(indicator.NewRSI(14))
	reg.Register(indicator.NewMACD(12, 26, 9))
	reg.Register(indicator.NewBollingerBands(20, 2))
	return NewBacktestEngine(reg)
}

func benchmarkStrategy() domain.Strategy {
	return domain.Strategy{
		EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":30}`),
		ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":"<","value":70}`),
	}
}

func BenchmarkBacktestEngine_1k(b *testing.B)     { benchmarkKlines(b, 1000) }
func BenchmarkBacktestEngine_10k(b *testing.B)    { benchmarkKlines(b, 10000) }
func BenchmarkBacktestEngine_100k(b *testing.B)   { benchmarkKlines(b, 100000) }

func benchmarkKlines(b *testing.B, n int) {
	engine := benchmarkEngine()
	strategy := benchmarkStrategy()
	candles := buildSineCandles(n, decimal.NewFromInt(50000), 42)

	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		_, err := engine.Run(context.Background(), EngineInput{
			Strategy:       strategy,
			Pair:           "BTCUSDT",
			Klines:         candles,
			InitialCapital: decimal.NewFromInt(1000),
		})
		if err != nil {
			b.Fatal(err)
		}
	}
}
