package indicator

type BollingerBands struct {
	Period int
	StdDev float64
}

func NewBollingerBands(period int, stdDev float64) *BollingerBands {
	return &BollingerBands{Period: period, StdDev: stdDev}
}

func (b *BollingerBands) Name() string { return "bollinger" }

func (b *BollingerBands) Compute(values []float64) []float64 {
	n := len(values)
	result := make([]float64, n)
	if n < b.Period {
		return result
	}

	for i := b.Period - 1; i < n; i++ {
		var sum, meanVal float64
		for j := i - b.Period + 1; j <= i; j++ {
			sum += values[j]
		}
		meanVal = sum / float64(b.Period)

		var varianceSum float64
		for j := i - b.Period + 1; j <= i; j++ {
			diff := values[j] - meanVal
			varianceSum += diff * diff
		}
		std := b.StdDev * sqrt(varianceSum / float64(b.Period))

		result[i] = (values[i] - meanVal) / (std + 1e-10)
	}

	return result
}

func sqrt(x float64) float64 {
	if x <= 0 {
		return 0
	}
	z := x / 2.0
	for i := 0; i < 10; i++ {
		z -= (z*z - x) / (2 * z)
	}
	return z
}
