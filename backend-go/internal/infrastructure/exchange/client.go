package exchange

import (
	"context"
	"time"

	"github.com/tradex/backend-go/internal/domain"
)

type KlineClient interface {
	FetchKlines(ctx context.Context, pair, timeframe string, start, end time.Time) ([]domain.Candle, error)
	Ping(ctx context.Context) error
}
