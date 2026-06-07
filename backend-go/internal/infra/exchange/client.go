package exchange

import (
	"context"
	"time"

	"tradex/internal/domain"
)

type KlineClient interface {
	FetchKlines(ctx context.Context, pair, timeframe string, start, end time.Time) ([]domain.Kline, error)
	Ping(ctx context.Context) error
}
