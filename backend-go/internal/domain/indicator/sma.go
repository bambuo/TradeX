package indicator

type SMA struct {
	Period int
}

func NewSMA(period int) *SMA {
	return &SMA{Period: period}
}

func (s *SMA) Name() string { return "sma" }

func (s *SMA) Compute(values []float64) []float64 {
	n := len(values)
	result := make([]float64, n)
	if n < s.Period {
		return result
	}

	var sum float64
	for i := 0; i < s.Period; i++ {
		sum += values[i]
	}
	result[s.Period-1] = sum / float64(s.Period)

	for i := s.Period; i < n; i++ {
		sum += values[i] - values[i-s.Period]
		result[i] = sum / float64(s.Period)
	}

	return result
}
