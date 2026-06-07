package backtest

import (
	"fmt"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

type BacktestTask struct {
	ID             uuid.UUID          `json:"id"`
	StrategyID     uuid.UUID          `json:"strategyId"`
	StrategyName   string             `json:"strategyName"`
	CreatedBy      uuid.UUID          `json:"createdBy"`
	ExchangeID     uuid.UUID          `json:"exchangeId"`
	Pair           string             `json:"pair"`
	Timeframe      string             `json:"timeframe"`
	InitialCapital decimal.Decimal    `json:"initialCapital"`
	PositionSize   *decimal.Decimal   `json:"positionSize,omitempty"`
	StartAt        time.Time          `json:"startAt"`
	EndAt          time.Time          `json:"endAt"`
	CompletedAt    *time.Time         `json:"completedAt,omitempty"`
	Status         BacktestTaskStatus `json:"status"`
	Phase          *BacktestPhase     `json:"phase,omitempty"`
	CreatedAt      time.Time          `json:"createdAt"`
}

func (t *BacktestTask) Validate() error {
	if !t.EndAt.After(t.StartAt) {
		return fmt.Errorf("结束时间必须晚于开始时间")
	}
	return nil
}

func (t *BacktestTask) Cancel() error {
	if t.Status == TaskStatusRunning || t.Status == TaskStatusPending {
		t.Status = TaskStatusCancelled
		t.Phase = nil
		return nil
	}
	return fmt.Errorf("%w: 任务已结束（%s），无法取消", domain.ErrConflict, t.Status)
}

func (t *BacktestTask) Start(phase BacktestPhase) error {
	if t.Status != TaskStatusPending {
		return fmt.Errorf("%w: 只能从 Pending 状态启动，当前 %s", domain.ErrConflict, t.Status)
	}
	t.Status = TaskStatusRunning
	t.Phase = &phase
	return nil
}

func (t *BacktestTask) Complete() error {
	if t.Status != TaskStatusRunning {
		return fmt.Errorf("%w: 只能在 Running 状态完成，当前 %s", domain.ErrConflict, t.Status)
	}
	t.Status = TaskStatusCompleted
	now := time.Now()
	t.CompletedAt = &now
	return nil
}

func (t *BacktestTask) Fail() error {
	if t.Status != TaskStatusRunning && t.Status != TaskStatusPending {
		return fmt.Errorf("%w: 无法从 %s 状态切换到 Failed", domain.ErrConflict, t.Status)
	}
	t.Status = TaskStatusFailed
	return nil
}
