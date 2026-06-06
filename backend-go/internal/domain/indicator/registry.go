package indicator

import "tradex/internal/domain"

type Registry struct {
	indicators map[string]Indicator
	results    map[string][]float64
}

type Indicator interface {
	Name() string
	Compute(values []float64) []float64
}

func NewRegistry() *Registry {
	return &Registry{
		indicators: make(map[string]Indicator),
		results:    make(map[string][]float64),
	}
}

func (r *Registry) Register(ind Indicator) {
	r.indicators[ind.Name()] = ind
}

func (r *Registry) ComputeAll(candles []domain.Candle) map[string][]float64 {
	close := candlesToF64Close(candles)
	volume := candlesToF64Volume(candles)

	r.results = make(map[string][]float64, len(r.indicators))
	for name, ind := range r.indicators {
		switch name {
		case "volume_sma":
			vals := make([]float64, len(volume))
			copy(vals, volume)
			r.results[name] = ind.Compute(vals)
		default:
			vals := make([]float64, len(close))
			copy(vals, close)
			r.results[name] = ind.Compute(vals)
		}
	}
	return r.results
}

func (r *Registry) GetValue(name string, index int) (float64, bool) {
	vals, ok := r.results[name]
	if !ok || index >= len(vals) {
		return 0, false
	}
	return vals[index], true
}

func (r *Registry) Indicators() []string {
	names := make([]string, 0, len(r.indicators))
	for n := range r.indicators {
		names = append(names, n)
	}
	return names
}

func candlesToF64Close(candles []domain.Candle) []float64 {
	out := make([]float64, len(candles))
	for i, c := range candles {
		out[i], _ = c.Close.Float64()
	}
	return out
}

func candlesToF64Volume(candles []domain.Candle) []float64 {
	out := make([]float64, len(candles))
	for i, c := range candles {
		out[i], _ = c.Volume.Float64()
	}
	return out
}
