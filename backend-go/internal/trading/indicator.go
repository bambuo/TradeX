package trading

import (
	"math"

	"github.com/shopspring/decimal"
)

type indicatorCompute func(window KlineWindow) decimal.Decimal

type indicatorRegistry struct {
	indicators map[string]indicatorCompute
}

func NewIndicatorRegistry() *indicatorRegistry {
	r := &indicatorRegistry{indicators: make(map[string]indicatorCompute)}
	r.registerBuiltin()
	return r
}

func (r *indicatorRegistry) register(name string, fn indicatorCompute) {
	r.indicators[name] = fn
}

func (r *indicatorRegistry) ComputeAll(window KlineWindow) map[string]decimal.Decimal {
	result := make(map[string]decimal.Decimal, len(r.indicators))
	for name, fn := range r.indicators {
		result[name] = fn(window)
	}
	return result
}

func (r *indicatorRegistry) registerBuiltin() {
	r.register("close", func(w KlineWindow) decimal.Decimal { return w.Close })
	r.register("open", func(w KlineWindow) decimal.Decimal { return w.Open })
	r.register("high", func(w KlineWindow) decimal.Decimal { return w.High })
	r.register("low", func(w KlineWindow) decimal.Decimal { return w.Low })

	r.register("sma_5", sma(5))
	r.register("sma_10", sma(10))
	r.register("sma_20", sma(20))
	r.register("sma_50", sma(50))
	r.register("sma_200", sma(200))

	r.register("ema_5", ema(5))
	r.register("ema_10", ema(10))
	r.register("ema_12", ema(12))
	r.register("ema_20", ema(20))
	r.register("ema_26", ema(26))

	r.register("rsi_14", rsi(14))

	r.register("bb_upper_20", bollingerBand(20, 2, true))
	r.register("bb_lower_20", bollingerBand(20, 2, false))

	r.register("macd", macd(12, 26, 9))
	r.register("macd_signal", macdSignal(12, 26, 9))
	r.register("macd_histogram", macdHistogram(12, 26, 9))

	r.register("stoch_k_14", stochastic(14, 3, true))
	r.register("stoch_d_14", stochastic(14, 3, false))
}

func sma(period int) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		prices := w.Prices
		n := len(prices)
		if n < period {
			return decimal.Zero
		}
		sum := decimal.Zero
		for i := n - period; i < n; i++ {
			sum = sum.Add(prices[i])
		}
		return sum.Div(decimal.NewFromInt(int64(period)))
	}
}

func ema(period int) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		prices := w.Prices
		n := len(prices)
		if n < period {
			return decimal.Zero
		}
		k := decimal.NewFromFloat(2.0 / float64(period+1))
		ema := decimal.Zero
		for i := 0; i < period; i++ {
			ema = ema.Add(prices[i])
		}
		ema = ema.Div(decimal.NewFromInt(int64(period)))
		for i := period; i < n; i++ {
			ema = prices[i].Sub(ema).Mul(k).Add(ema)
		}
		return ema
	}
}

func rsi(period int) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		prices := w.Prices
		n := len(prices)
		if n < period+1 {
			return decimal.NewFromInt(50)
		}
		gain := decimal.Zero
		loss := decimal.Zero
		for i := n - period; i < n; i++ {
			diff := prices[i].Sub(prices[i-1])
			if diff.IsPositive() {
				gain = gain.Add(diff)
			} else {
				loss = loss.Add(diff.Abs())
			}
		}
		avgGain := gain.Div(decimal.NewFromInt(int64(period)))
		avgLoss := loss.Div(decimal.NewFromInt(int64(period)))
		if avgLoss.IsZero() {
			return decimal.NewFromInt(100)
		}
		rs := avgGain.Div(avgLoss)
		return decimal.NewFromInt(100).Sub(decimal.NewFromInt(100).Div(rs.Add(decimal.NewFromInt(1))))
	}
}

func bollingerBand(period int, stddev float64, isUpper bool) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		prices := w.Prices
		n := len(prices)
		if n < period {
			return decimal.Zero
		}
		mean := sma(period)(w)
		var sum float64
		for i := n - period; i < n; i++ {
			f, _ := prices[i].Sub(mean).Float64()
			sum += f * f
		}
		variance := sum / float64(period)
		std := math.Sqrt(variance)
		band := mean.Add(decimal.NewFromFloat(std * stddev))
		if !isUpper {
			band = mean.Sub(decimal.NewFromFloat(std * stddev))
		}
		return band
	}
}

func macd(fast, slow, signal int) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		fastEMA := ema(fast)(w)
		slowEMA := ema(slow)(w)
		return fastEMA.Sub(slowEMA)
	}
}

func macdSignal(fast, slow, signal int) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		macdLine := macd(fast, slow, signal)(w)
		prices := w.Prices
		n := len(prices)
		if n < slow+signal {
			return decimal.Zero
		}
		k := decimal.NewFromFloat(2.0 / float64(signal+1))
		sig := macdLine
		start := n - signal
		if start < slow {
			start = slow
		}
		for i := start; i < n; i++ {
			currentMACD := ema(fast)(KlineWindow{
				Prices: prices[:i+1],
				Close:  prices[i],
			}).Sub(ema(slow)(KlineWindow{
				Prices: prices[:i+1],
				Close:  prices[i],
			}))
			sig = currentMACD.Sub(sig).Mul(k).Add(sig)
		}
		return sig
	}
}

func macdHistogram(fast, slow, signal int) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		return macd(fast, slow, signal)(w).Sub(macdSignal(fast, slow, signal)(w))
	}
}

func stochastic(kPeriod, smoothK int, isK bool) indicatorCompute {
	return func(w KlineWindow) decimal.Decimal {
		prices := w.Prices
		n := len(prices)
		if n < kPeriod {
			return decimal.Zero
		}
		start := n - kPeriod
		low := prices[start]
		high := prices[start]
		for i := start; i < n; i++ {
			if prices[i].LessThan(low) {
				low = prices[i]
			}
			if prices[i].GreaterThan(high) {
				high = prices[i]
			}
		}
		range_ := high.Sub(low)
		if range_.IsZero() {
			return decimal.NewFromInt(50)
		}
		k := prices[n-1].Sub(low).Div(range_).Mul(decimal.NewFromInt(100))
		if isK {
			return k
		}
		if n < kPeriod+smoothK {
			return k
		}
		sum := decimal.Zero
		for i := n - smoothK; i < n; i++ {
			start2 := i - kPeriod + 1
			if start2 < 0 {
				start2 = 0
			}
			low2 := prices[start2]
			high2 := prices[start2]
			for j := start2; j <= i; j++ {
				if prices[j].LessThan(low2) {
					low2 = prices[j]
				}
				if prices[j].GreaterThan(high2) {
					high2 = prices[j]
				}
			}
			r2 := high2.Sub(low2)
			if r2.IsZero() {
				sum = sum.Add(decimal.NewFromInt(50))
			} else {
				sum = sum.Add(prices[i].Sub(low2).Div(r2).Mul(decimal.NewFromInt(100)))
			}
		}
		return sum.Div(decimal.NewFromInt(int64(smoothK)))
	}
}
