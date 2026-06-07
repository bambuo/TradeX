package domain

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

type BacktestTask struct {
	ID             uuid.UUID
	StrategyID     uuid.UUID
	StrategyName   string
	CreatedBy      uuid.UUID
	ExchangeID     uuid.UUID
	Pair           string
	Timeframe      string
	InitialCapital decimal.Decimal
	PositionSize   *decimal.Decimal
	StartAt        time.Time
	EndAt          time.Time
	CompletedAt    *time.Time
	Status         BacktestTaskStatus
	Phase          *BacktestPhase
	CreatedAt      time.Time
}
