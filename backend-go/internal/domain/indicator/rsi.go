package indicator

type RSI struct {
	Period int
}

func NewRSI(period int) *RSI {
	return &RSI{Period: period}
}

func (r *RSI) Name() string { return "rsi" }

func (r *RSI) Compute(values []float64) []float64 {
	n := len(values)
	result := make([]float64, n)
	if n < r.Period+1 {
		return result
	}

	gains := make([]float64, n)
	losses := make([]float64, n)
	for i := 1; i < n; i++ {
		diff := values[i] - values[i-1]
		if diff > 0 {
			gains[i] = diff
		} else {
			losses[i] = -diff
		}
	}

	avgGain := mean(gains[1 : r.Period+1])
	avgLoss := mean(losses[1 : r.Period+1])

	if avgLoss == 0 {
		result[r.Period] = 100
	} else {
		rs := avgGain / avgLoss
		result[r.Period] = 100 - (100 / (1 + rs))
	}

	for i := r.Period + 1; i < n; i++ {
		avgGain = (avgGain*float64(r.Period-1) + gains[i]) / float64(r.Period)
		avgLoss = (avgLoss*float64(r.Period-1) + losses[i]) / float64(r.Period)

		if avgLoss == 0 {
			result[i] = 100
		} else {
			rs := avgGain / avgLoss
			result[i] = 100 - (100 / (1 + rs))
		}
	}

	return result
}

func mean(vals []float64) float64 {
	var sum float64
	for _, v := range vals {
		sum += v
	}
	return sum / float64(len(vals))
}
