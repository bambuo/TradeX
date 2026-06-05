package indicator

type MACD struct {
	FastPeriod   int
	SlowPeriod   int
	SignalPeriod int
}

func NewMACD(fast, slow, signal int) *MACD {
	return &MACD{
		FastPeriod:   fast,
		SlowPeriod:   slow,
		SignalPeriod: signal,
	}
}

func (m *MACD) Name() string { return "macd" }

func (m *MACD) Compute(values []float64) []float64 {
	n := len(values)
	result := make([]float64, n)
	if n < m.SlowPeriod+m.SignalPeriod {
		return result
	}

	fastEMA := NewEMA(m.FastPeriod).Compute(values)
	slowEMA := NewEMA(m.SlowPeriod).Compute(values)

	macdLine := make([]float64, n)
	for i := 0; i < n; i++ {
		macdLine[i] = fastEMA[i] - slowEMA[i]
	}

	signal := NewEMA(m.SignalPeriod).Compute(macdLine)

	for i := 0; i < n; i++ {
		result[i] = macdLine[i] - signal[i]
	}

	return result
}
