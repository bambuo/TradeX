package engine

import (
	"math"
	"math/rand"
	"time"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

func buildSineCandles(count int, basePrice decimal.Decimal, seed int64) []domain.Candle {
	rng := rand.New(rand.NewSource(seed))
	candles := make([]domain.Candle, count)
	price := basePrice
	t0 := time.Date(2024, 1, 1, 0, 0, 0, 0, time.UTC)

	for i := 0; i < count; i++ {
		trendVal := math.Sin(float64(i)/20.0) * 1500
		noiseVal := (rng.Float64() - 0.5) * 300

		trend := decimal.NewFromFloat(trendVal)
		noise := decimal.NewFromFloat(noiseVal)

		open := price
		close_ := basePrice.Add(trend).Add(noise)

		halfAbsNoise := absDec(noise).Mul(decimal.NewFromFloat(0.5))

		high := maxDec(open, close_).Add(halfAbsNoise)
		low := minDec(open, close_).Sub(halfAbsNoise)

		volume := decimal.NewFromFloat(rng.Float64()*1000 + 100)

		candles[i] = domain.Candle{
			Timestamp: t0.Add(time.Duration(i) * time.Hour),
			Open:      open,
			High:      high,
			Low:       low,
			Close:     close_,
			Volume:    volume,
		}

		price = close_
	}

	return candles
}

func maxDec(a, b decimal.Decimal) decimal.Decimal {
	if a.GreaterThanOrEqual(b) {
		return a
	}
	return b
}

func minDec(a, b decimal.Decimal) decimal.Decimal {
	if a.LessThanOrEqual(b) {
		return a
	}
	return b
}

func absDec(d decimal.Decimal) decimal.Decimal {
	if d.IsNegative() {
		return d.Neg()
	}
	return d
}
