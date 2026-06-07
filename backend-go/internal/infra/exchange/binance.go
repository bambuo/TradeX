package exchange

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"math/rand"
	"net/http"
	"strconv"
	"time"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

const maxRetries = 3

type binanceKline struct {
	OpenTime         int64
	Open             string
	High             string
	Low              string
	Close            string
	Volume           string
	CloseTime        int64
	QuoteAssetVolume string
	NumberOfTrades   int
	TakerBuyBaseVol  string
	TakerBuyQuoteVol string
	UnusedField      string
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
	stepMs := intervalMs(timeframe)
	cursor := start

	var all []domain.Candle
	seen := make(map[int64]bool)

	for cursor.Before(end) {
		url := fmt.Sprintf("%s/api/v3/klines?symbol=%s&interval=%s&startTime=%d&endTime=%d&limit=%d",
			b.baseURL, pair, timeframe, cursor.UnixMilli(), end.UnixMilli(), limit)

		batch, err := b.fetchBatch(ctx, url)
		if err != nil {
			return nil, err
		}
		if len(batch) == 0 {
			break
		}

		lastTs := cursor
		for _, c := range batch {
			ts := c.Timestamp
			if ts.Before(start) || ts.After(end) {
				continue
			}
			if seen[ts.UnixMilli()] {
				continue
			}
			seen[ts.UnixMilli()] = true
			all = append(all, c)
			if ts.After(lastTs) {
				lastTs = ts
			}
		}

		if !lastTs.After(cursor) {
			break
		}
		cursor = lastTs.Add(time.Duration(stepMs) * time.Millisecond)
	}

	return all, nil
}

func (b *BinanceClient) fetchBatch(ctx context.Context, url string) ([]domain.Candle, error) {
	var lastErr error
	for attempt := 0; attempt <= maxRetries; attempt++ {
		if attempt > 0 {
			delay := time.Duration(100*attempt+rand.Intn(100)) * time.Millisecond
			select {
			case <-ctx.Done():
				return nil, ctx.Err()
			case <-time.After(delay):
			}
		}

		req, err := http.NewRequestWithContext(ctx, "GET", url, nil)
		if err != nil {
			lastErr = fmt.Errorf("创建请求失败: %w", err)
			continue
		}

		resp, err := b.client.Do(req)
		if err != nil {
			lastErr = fmt.Errorf("获取K线失败: %w", err)
			continue
		}

		body, err := io.ReadAll(resp.Body)
		resp.Body.Close()
		if err != nil {
			lastErr = fmt.Errorf("读取响应失败: %w", err)
			continue
		}

		if resp.StatusCode != 200 {
			lastErr = fmt.Errorf("Binance API 错误 %d: %s", resp.StatusCode, string(body))
			continue
		}

		return parseKlines(body)
	}

	return nil, fmt.Errorf("获取K线失败（已重试%d次）: %w", maxRetries, lastErr)
}

func parseKlines(body []byte) ([]domain.Candle, error) {
	var raw [][]json.RawMessage
	if err := json.Unmarshal(body, &raw); err != nil {
		return nil, fmt.Errorf("解析K线失败: %w", err)
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

func intervalMs(tf string) int64 {
	switch tf {
	case "1m":
		return 60_000
	case "5m":
		return 300_000
	case "15m":
		return 900_000
	case "30m":
		return 1_800_000
	case "1h":
		return 3_600_000
	case "4h":
		return 14_400_000
	case "1d":
		return 86_400_000
	default:
		return 60_000
	}
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
