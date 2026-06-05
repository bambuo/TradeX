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
	ExchangeID     string
	Pair           string
	Timeframe      string
	InitialCapital decimal.Decimal
	PositionSize   *decimal.Decimal
	FeeRate        decimal.Decimal
	StartAt        time.Time
	EndAt          time.Time
	CompletedAt    *time.Time
	Status         BacktestTaskStatus
	Phase          *BacktestPhase
	Progress       int
	ErrorMessage   *string
	CreatedAt      time.Time
	UpdatedAt      time.Time
}

func (t *BacktestTask) SetStatus(s BacktestTaskStatus) {
	t.Status = s
	t.UpdatedAt = time.Now()
}

func (t *BacktestTask) SetPhase(p BacktestPhase) {
	t.Phase = &p
	t.UpdatedAt = time.Now()
}

func (t *BacktestTask) SetProgress(p int) {
	t.Progress = p
	t.UpdatedAt = time.Now()
}

func (t *BacktestTask) Fail(errMsg string) {
	t.Status = TaskStatusFailed
	t.ErrorMessage = &errMsg
	t.UpdatedAt = time.Now()
}

func (t *BacktestTask) Cancel() {
	t.Status = TaskStatusCancelled
	t.UpdatedAt = time.Now()
}
