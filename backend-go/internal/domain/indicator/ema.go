package indicator

type EMA struct {
	Period int
}

func NewEMA(period int) *EMA {
	return &EMA{Period: period}
}

func (e *EMA) Name() string { return "ema" }

func (e *EMA) Compute(values []float64) []float64 {
	n := len(values)
	result := make([]float64, n)
	if n < e.Period {
		return result
	}

	multiplier := 2.0 / float64(e.Period+1)

	var sum float64
	for i := 0; i < e.Period; i++ {
		sum += values[i]
	}
	result[e.Period-1] = sum / float64(e.Period)

	for i := e.Period; i < n; i++ {
		result[i] = (values[i]-result[i-1])*multiplier + result[i-1]
	}

	return result
}
