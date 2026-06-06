package storage

import (
	"context"
	"sync"
	"time"

	"tradex/internal/domain"
)

type KlineCache interface {
	Get(ctx context.Context, pair, timeframe string, start, end time.Time) ([]domain.Candle, bool)
	Set(ctx context.Context, pair, timeframe string, candles []domain.Candle)
}

type memoryKlineCache struct {
	mu       sync.RWMutex
	entries  map[string]cacheEntry
	ttl      time.Duration
}

type cacheEntry struct {
	candles   []domain.Candle
	expiresAt time.Time
}

func NewKlineCache(ttl time.Duration) KlineCache {
	return &memoryKlineCache{
		entries: make(map[string]cacheEntry),
		ttl:     ttl,
	}
}

func (c *memoryKlineCache) cacheKey(pair, timeframe string) string {
	return pair + ":" + timeframe
}

func (c *memoryKlineCache) Get(_ context.Context, pair, timeframe string, start, end time.Time) ([]domain.Candle, bool) {
	c.mu.RLock()
	entry, ok := c.entries[c.cacheKey(pair, timeframe)]
	c.mu.RUnlock()

	if !ok || time.Now().After(entry.expiresAt) {
		return nil, false
	}

	filtered := make([]domain.Candle, 0, len(entry.candles))
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

func (c *memoryKlineCache) Set(_ context.Context, pair, timeframe string, candles []domain.Candle) {
	c.mu.Lock()
	defer c.mu.Unlock()

	deduped := make([]domain.Candle, 0, len(candles))
	seen := make(map[int64]struct{})
	for _, candle := range candles {
		ts := candle.Timestamp.Unix()
		if _, ok := seen[ts]; ok {
			continue
		}
		seen[ts] = struct{}{}
		deduped = append(deduped, candle)
	}

	c.entries[c.cacheKey(pair, timeframe)] = cacheEntry{
		candles:   deduped,
		expiresAt: time.Now().Add(c.ttl),
	}
}
