package persistence

import (
	"context"
	"sync"
	"time"

	"github.com/google/uuid"

	"tradex/internal/domain"
)

type KlineCache interface {
	Get(ctx context.Context, exchangeID uuid.UUID, pair, timeframe string, start, end time.Time) ([]domain.Kline, bool)
	Set(ctx context.Context, exchangeID uuid.UUID, pair, timeframe string, candles []domain.Kline)
}

type memoryKlineCache struct {
	mu      sync.RWMutex
	entries map[string]cacheEntry
	ttl     time.Duration
}

type cacheEntry struct {
	candles   []domain.Kline
	expiresAt time.Time
}

func NewKlineCache(ttl time.Duration) KlineCache {
	return &memoryKlineCache{
		entries: make(map[string]cacheEntry),
		ttl:     ttl,
	}
}

func (c *memoryKlineCache) cacheKey(exchangeID uuid.UUID, pair, timeframe string) string {
	return exchangeID.String() + ":" + pair + ":" + timeframe
}

func (c *memoryKlineCache) Get(_ context.Context, exchangeID uuid.UUID, pair, timeframe string, start, end time.Time) ([]domain.Kline, bool) {
	c.mu.RLock()
	entry, ok := c.entries[c.cacheKey(exchangeID, pair, timeframe)]
	c.mu.RUnlock()

	if !ok || time.Now().After(entry.expiresAt) {
		return nil, false
	}

	// 缓存数据必须完整覆盖到 endAt 才算命中
	if len(entry.candles) == 0 || entry.candles[len(entry.candles)-1].Timestamp.Before(end) {
		return nil, false
	}

	filtered := make([]domain.Kline, 0, len(entry.candles))
	for _, candle := range entry.candles {
		if (candle.Timestamp.Equal(start) || candle.Timestamp.After(start)) &&
			(candle.Timestamp.Equal(end) || candle.Timestamp.Before(end)) {
			filtered = append(filtered, candle)
		}
	}

	if len(filtered) == 0 {
		return nil, false
	}
	return filtered, true
}

func (c *memoryKlineCache) Set(_ context.Context, exchangeID uuid.UUID, pair, timeframe string, candles []domain.Kline) {
	c.mu.Lock()
	defer c.mu.Unlock()

	deduped := make([]domain.Kline, 0, len(candles))
	seen := make(map[int64]struct{})
	for _, candle := range candles {
		ts := candle.Timestamp.Unix()
		if _, ok := seen[ts]; ok {
			continue
		}
		seen[ts] = struct{}{}
		deduped = append(deduped, candle)
	}

	c.entries[c.cacheKey(exchangeID, pair, timeframe)] = cacheEntry{
		candles:   deduped,
		expiresAt: time.Now().Add(c.ttl),
	}
}
