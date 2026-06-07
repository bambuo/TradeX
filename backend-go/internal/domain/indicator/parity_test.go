package indicator

import (
	"math"
	"testing"
)

func TestSMA_Parity_WithKnownValues(t *testing.T) {
	sma := NewSMA(3)
	input := []float64{1, 2, 3, 4, 5, 6, 7, 8, 9, 10}
	output := sma.Compute(input)

	expected := []float64{0, 0, 2, 3, 4, 5, 6, 7, 8, 9}
	for i := range expected {
		if expected[i] == 0 {
			if output[i] != 0 {
				t.Errorf("index %d: expected 0 (NaN), got %f", i, output[i])
			}
			continue
		}
		if math.Abs(output[i]-expected[i]) > 0.0001 {
			t.Errorf("index %d: expected %f, got %f", i, expected[i], output[i])
		}
	}
}

func TestSMA_MonotonicInput_MonotonicOutput(t *testing.T) {
	sma := NewSMA(10)
	input := make([]float64, 100)
	for i := range input {
		input[i] = float64(i)
	}
	output := sma.Compute(input)

	for i := 10; i < len(output); i++ {
		if output[i] <= output[i-1] {
			t.Errorf("SMA should be monotonically increasing for linear input at index %d: %f <= %f",
				i, output[i], output[i-1])
		}
	}
}

func TestEMA_Parity_WithKnownValues(t *testing.T) {
	ema := NewEMA(3)
	input := []float64{1, 2, 3, 4, 5, 6, 7, 8, 9, 10}
	output := ema.Compute(input)

	expected := []float64{0, 0, 2, 3, 4, 5, 6, 7, 8, 9}
	for i, exp := range expected {
		if exp == 0 {
			continue
		}
		if math.Abs(output[i]-exp) > 0.0001 {
			t.Errorf("EMA[%d]: expected %f, got %f", i, exp, output[i])
		}
	}
}

func TestRSI_Parity_BoundedBetweenZeroAndHundred(t *testing.T) {
	rsi := NewRSI(14)
	input := make([]float64, 200)
	for i := range input {
		input[i] = float64(100 + i%40)
	}
	output := rsi.Compute(input)

	foundValid := false
	for _, v := range output {
		if v != 0 {
			foundValid = true
			if v < 0 || v > 100 {
				t.Errorf("RSI out of range [0,100]: %f", v)
			}
		}
	}
	if !foundValid {
		t.Error("RSI produced no valid values")
	}
}

func TestRSI_UpTrend_GreaterThanFifty(t *testing.T) {
	rsi := NewRSI(5)
	input := make([]float64, 100)
	for i := range input {
		input[i] = float64(100 + i)
	}
	output := rsi.Compute(input)

	highCount := 0
	for _, v := range output {
		if v > 60 {
			highCount++
		}
	}
	if highCount < 10 {
		t.Errorf("RSI should be >60 for strongly uptrending data, only %d values", highCount)
	}
}

func TestMACD_Parity_BasicProperties(t *testing.T) {
	macd := NewMACD(12, 26, 9)
	input := make([]float64, 100)
	for i := range input {
		input[i] = float64(100 + i)
	}
	output := macd.Compute(input)

	if len(output) == 0 {
		t.Fatal("MACD output is empty")
	}

	firstValid := -1
	for i, v := range output {
		if v != 0 {
			firstValid = i
			break
		}
	}
	if firstValid < 0 {
		t.Fatal("MACD produced no valid values")
	}
	t.Logf("MACD first valid at index %d", firstValid)
}

func TestBollingerBands_Parity_BasicProperties(t *testing.T) {
	bb := NewBollingerBands(20, 2)
	input := make([]float64, 100)
	for i := range input {
		input[i] = 100 + float64(i)*0.5
	}
	output := bb.Compute(input)

	if len(output) == 0 {
		t.Fatal("BB output is empty")
	}

	firstValid := -1
	for i, v := range output {
		if v != 0 {
			firstValid = i
			break
		}
	}
	if firstValid < 0 {
		t.Fatal("BB produced no valid values")
	}
	t.Logf("BB first valid at index %d", firstValid)
}

func TestStochastic_Parity_BasicProperties(t *testing.T) {
	stoch := NewStochastic(5, 3)
	input := make([]float64, 100)
	for i := range input {
		input[i] = float64(100 + i)
	}
	output := stoch.Compute(input)

	if len(output) == 0 {
		t.Fatal("Stochastic output is empty")
	}
	firstValid := -1
	for i, v := range output {
		if v != 0 {
			firstValid = i
			break
		}
	}
	if firstValid < 0 {
		t.Fatal("Stochastic produced no valid values")
	}
	t.Logf("Stochastic first valid at index %d", firstValid)
}
