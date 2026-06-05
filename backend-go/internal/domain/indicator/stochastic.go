package indicator

type Stochastic struct {
	KPeriod int
	DPeriod int
}

func NewStochastic(kPeriod, dPeriod int) *Stochastic {
	return &Stochastic{KPeriod: kPeriod, DPeriod: dPeriod}
}

func (s *Stochastic) Name() string { return "stochastic" }

func (s *Stochastic) Compute(values []float64) []float64 {
	n := len(values)
	result := make([]float64, n)
	if n < s.KPeriod+s.DPeriod {
		return result
	}

	lowest := func(start, end int) float64 {
		min := values[start]
		for i := start + 1; i <= end; i++ {
			if values[i] < min {
				min = values[i]
			}
		}
		return min
	}

	highest := func(start, end int) float64 {
		max := values[start]
		for i := start + 1; i <= end; i++ {
			if values[i] > max {
				max = values[i]
			}
		}
		return max
	}

	kValues := make([]float64, n)
	for i := s.KPeriod - 1; i < n; i++ {
		low := lowest(i-s.KPeriod+1, i)
		high := highest(i-s.KPeriod+1, i)
		range_ := high - low
		if range_ == 0 {
			kValues[i] = 50
		} else {
			kValues[i] = ((values[i] - low) / range_) * 100
		}
	}

	dSMA := NewSMA(s.DPeriod).Compute(kValues)
	for i := 0; i < n; i++ {
		result[i] = kValues[i] - dSMA[i]
	}

	return result
}
