package exchange

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strconv"
	"time"

	"github.com/shopspring/decimal"

	"github.com/tradex/backend-go/internal/domain"
)

type binanceKline struct {
	OpenTime                int64
	Open                    string
	High                    string
	Low                     string
	Close                   string
	Volume                  string
	CloseTime               int64
	QuoteAssetVolume        string
	NumberOfTrades          int
	TakerBuyBaseVol         string
	TakerBuyQuoteVol        string
	UnusedField             string
}

type BinanceClient struct {
	baseURL string
	client  *http.Client
}

func NewBinanceClient() *BinanceClient {
	return &BinanceClient{
		baseURL: "https://api.binance.com",
		client:  &http.Client{Timeout: 30 * time.Second},
	}
}

func (b *BinanceClient) FetchKlines(ctx context.Context, pair, timeframe string, start, end time.Time) ([]domain.Candle, error) {
	limit := 1000
	url := fmt.Sprintf("%s/api/v3/klines?symbol=%s&interval=%s&startTime=%d&endTime=%d&limit=%d",
		b.baseURL, pair, timeframe, start.UnixMilli(), end.UnixMilli(), limit)

	req, err := http.NewRequestWithContext(ctx, "GET", url, nil)
	if err != nil {
		return nil, fmt.Errorf("create request: %w", err)
	}

	resp, err := b.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("fetch klines: %w", err)
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("read body: %w", err)
	}

	if resp.StatusCode != 200 {
		return nil, fmt.Errorf("binance api error %d: %s", resp.StatusCode, string(body))
	}

	var raw [][]json.RawMessage
	if err := json.Unmarshal(body, &raw); err != nil {
		return nil, fmt.Errorf("unmarshal klines: %w", err)
	}

	candles := make([]domain.Candle, 0, len(raw))
	for _, item := range raw {
		if len(item) < 11 {
			continue
		}

		openTime, _ := strconv.ParseInt(string(item[0]), 10, 64)
		openStr, _ := strconv.Unquote(string(item[1]))
		highStr, _ := strconv.Unquote(string(item[2]))
		lowStr, _ := strconv.Unquote(string(item[3]))
		closeStr, _ := strconv.Unquote(string(item[4]))
		volumeStr, _ := strconv.Unquote(string(item[5]))

		open, _ := decimal.NewFromString(openStr)
		high, _ := decimal.NewFromString(highStr)
		low, _ := decimal.NewFromString(lowStr)
		close_, _ := decimal.NewFromString(closeStr)
		volume, _ := decimal.NewFromString(volumeStr)

		candles = append(candles, domain.Candle{
			Timestamp: time.UnixMilli(openTime),
			Open:      open,
			High:      high,
			Low:       low,
			Close:     close_,
			Volume:    volume,
		})
	}

	return candles, nil
}

func (b *BinanceClient) Ping(ctx context.Context) error {
	req, err := http.NewRequestWithContext(ctx, "GET", b.baseURL+"/api/v3/ping", nil)
	if err != nil {
		return err
	}
	resp, err := b.client.Do(req)
	if err != nil {
		return err
	}
	resp.Body.Close()
	return nil
}

func TimeframeToBinanceInterval(tf string) string {
	mapping := map[string]string{
		"1m":  "1m",
		"5m":  "5m",
		"15m": "15m",
		"30m": "30m",
		"1h":  "1h",
		"4h":  "4h",
		"1d":  "1d",
		"1w":  "1w",
	}
	if mapped, ok := mapping[tf]; ok {
		return mapped
	}
	return tf
}
