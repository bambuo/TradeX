package backtest

import (
	"context"
	"encoding/json"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"

	"tradex/internal/domain"
	bt "tradex/internal/domain/backtest"
	"tradex/internal/domain/indicator"
	"tradex/internal/infra/analysis"
	"tradex/internal/infra/persistence"
)

// mockSchedulerRepo implements bt.BacktestRepository for testing
type mockSchedulerRepo struct {
	task          *bt.BacktestTask
	strategy      *domain.Strategy
	taskStatus    bt.BacktestTaskStatus
	analysisCount int
	resultSaved   bool
	analysisSaved bool
	statusUpdated bt.BacktestTaskStatus

	transactionErr error
	pendingTasks   []*bt.BacktestTask
	runningTasks   []*bt.BacktestTask
	acquireResult  bool
	acquireError   error
}

func (m *mockSchedulerRepo) CreateTask(_ context.Context, _ *bt.BacktestTask) error { return nil }
func (m *mockSchedulerRepo) GetTask(_ context.Context, id uuid.UUID) (*bt.BacktestTask, error) {
	if m.task == nil {
		return &bt.BacktestTask{ID: id, Status: m.taskStatus}, nil
	}
	return m.task, nil
}
func (m *mockSchedulerRepo) SaveTask(_ context.Context, task *bt.BacktestTask) error {
	m.statusUpdated = task.Status
	return nil
}
func (m *mockSchedulerRepo) ListTasks(_ context.Context, _ bt.TaskFilter) ([]*bt.BacktestTask, int, error) {
	return nil, 0, nil
}
func (m *mockSchedulerRepo) SaveResult(_ context.Context, _ uuid.UUID, _ *bt.BacktestResult, _ []bt.BacktestTrade) error {
	m.resultSaved = true
	return nil
}
func (m *mockSchedulerRepo) GetResult(_ context.Context, _ uuid.UUID) (*bt.BacktestResult, []bt.BacktestTrade, error) {
	return nil, nil, nil
}
func (m *mockSchedulerRepo) SaveAnalysisBatch(_ context.Context, _ uuid.UUID, _ []bt.BacktestKlineAnalysis) error {
	m.analysisSaved = true
	return nil
}
func (m *mockSchedulerRepo) GetAnalysis(_ context.Context, _ uuid.UUID, _, _ int) ([]bt.BacktestKlineAnalysis, error) {
	return nil, nil
}
func (m *mockSchedulerRepo) GetAnalysisCount(_ context.Context, _ uuid.UUID) (int, error) {
	return m.analysisCount, nil
}
func (m *mockSchedulerRepo) GetPendingTasks(_ context.Context) ([]*bt.BacktestTask, error) {
	return m.pendingTasks, nil
}
func (m *mockSchedulerRepo) GetRunningTasks(_ context.Context) ([]*bt.BacktestTask, error) {
	return m.runningTasks, nil
}
func (m *mockSchedulerRepo) GetStrategy(_ context.Context, _ uuid.UUID) (*domain.Strategy, error) {
	return m.strategy, nil
}
func (m *mockSchedulerRepo) ExecuteInTransaction(_ context.Context, fn func(bt.BacktestRepository) error) error {
	if m.transactionErr != nil {
		return m.transactionErr
	}
	return fn(m)
}
func (m *mockSchedulerRepo) ClaimTask(_ context.Context, id uuid.UUID, fromStatus bt.BacktestTaskStatus, phase bt.BacktestPhase) (bool, error) {
	if m.acquireError != nil {
		return false, m.acquireError
	}
	if m.acquireResult && m.task != nil {
		m.task.Status = bt.TaskStatusRunning
		m.task.Phase = &phase
	}
	return m.acquireResult, nil
}

type mockKlineClient struct {
	candles []domain.Kline
	err     error
}

func (m *mockKlineClient) FetchKlines(_ context.Context, _, _ string, _, _ time.Time) ([]domain.Kline, error) {
	return m.candles, m.err
}
func (m *mockKlineClient) Ping(_ context.Context) error { return nil }

func newTestScheduler(t *testing.T, repo *mockSchedulerRepo) *BacktestScheduler {
	reg := indicator.NewRegistry()
	reg.Register(indicator.NewSMA(3))
	reg.Register(indicator.NewRSI(5))

	return &BacktestScheduler{
		repo:          repo,
		queue:         NewTaskQueue(10),
		monitor:       NewResourceMonitor(context.Background(), 2, DefaultResourceMonitorConfig()),
		registry:      reg,
		klineCache:    persistence.NewKlineCache(10 * time.Minute),
		klineClient:   &mockKlineClient{candles: buildTestCandles(100)},
		tracker:       NewRunningBacktestTracker(),
		analysisStore: analysis.NewStore(),
		log:           zerolog.Nop(),
	}
}

func buildTestCandles(n int) []domain.Kline {
	candles := make([]domain.Kline, n)
	base := decimal.NewFromInt(50000)
	t0 := time.Date(2024, 1, 1, 0, 0, 0, 0, time.UTC)
	for i := range candles {
		price := base.Add(decimal.NewFromInt(int64(i)))
		candles[i] = domain.Kline{
			Timestamp: t0.Add(time.Duration(i) * time.Hour),
			Open:      price,
			High:      price.Add(decimal.NewFromInt(100)),
			Low:       price.Sub(decimal.NewFromInt(100)),
			Close:     price,
			Volume:    decimal.NewFromInt(1000),
		}
	}
	return candles
}

func TestScheduler_ExecuteTask_UsesExchangeID(t *testing.T) {
	exchangeID := uuid.New()

	repo := &mockSchedulerRepo{
		task: &bt.BacktestTask{
			ID:             uuid.New(),
			StrategyID:     uuid.New(),
			ExchangeID:     exchangeID,
			Pair:           "BTCUSDT",
			Timeframe:      "1h",
			InitialCapital: decimal.NewFromInt(1000),
			StartAt:        time.Date(2024, 1, 1, 0, 0, 0, 0, time.UTC),
			EndAt:          time.Date(2024, 1, 5, 0, 0, 0, 0, time.UTC),
			Status:         bt.TaskStatusPending,
		},
		strategy: &domain.Strategy{
			ID:             uuid.New(),
			Name:           "test-strategy",
			EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
			ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":100}`),
		},
		acquireResult: true,
	}

	s := newTestScheduler(t, repo)
	cfg := SchedulerConfig{
		MaxConcurrency:     2,
		TaskTimeoutMinutes: 30,
		FeeRate:            0.001,
	}

	// Execute task
	ctx := context.Background()
	s.executeTask(ctx, repo.task.ID, cfg)

	assert.Equal(t, bt.TaskStatusCompleted, repo.statusUpdated,
		"task should complete successfully")
}

func TestScheduler_ExecuteTask_TaskCancelledBeforeEngine_Aborts(t *testing.T) {
	taskID := uuid.New()
	repo := &mockSchedulerRepo{
		task: &bt.BacktestTask{
			ID:             taskID,
			Status:         bt.TaskStatusCancelled,
			Pair:           "BTCUSDT",
			Timeframe:      "1h",
			InitialCapital: decimal.NewFromInt(1000),
		},
		acquireResult: false,
	}

	s := newTestScheduler(t, repo)
	cfg := SchedulerConfig{MaxConcurrency: 2, TaskTimeoutMinutes: 30, FeeRate: 0.001}

	ctx := context.Background()
	s.executeTask(ctx, taskID, cfg)

	// Should not attempt to run engine
	assert.False(t, repo.resultSaved, "should not save result for cancelled task")
}

func TestScheduler_ExecuteTask_SaveAnalysisBatch(t *testing.T) {
	repo := &mockSchedulerRepo{
		task: &bt.BacktestTask{
			ID:             uuid.New(),
			StrategyID:     uuid.New(),
			ExchangeID:     uuid.New(),
			Pair:           "BTCUSDT",
			Timeframe:      "1h",
			InitialCapital: decimal.NewFromInt(1000),
			StartAt:        time.Date(2024, 1, 1, 0, 0, 0, 0, time.UTC),
			EndAt:          time.Date(2024, 1, 2, 0, 0, 0, 0, time.UTC),
			Status:         bt.TaskStatusPending,
		},
		strategy: &domain.Strategy{
			ID:             uuid.New(),
			Name:           "test",
			EntryCondition: json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":0}`),
			ExitCondition:  json.RawMessage(`{"operator":"","indicator":"RSI","comparison":">","value":100}`),
		},
		acquireResult: true,
	}

	s := newTestScheduler(t, repo)
	cfg := SchedulerConfig{MaxConcurrency: 2, TaskTimeoutMinutes: 30, FeeRate: 0.001}

	ctx := context.Background()
	s.executeTask(ctx, repo.task.ID, cfg)

	assert.True(t, repo.analysisSaved, "analysis batch should be saved")
	assert.True(t, repo.resultSaved, "result should be saved")
	assert.Equal(t, bt.TaskStatusCompleted, repo.statusUpdated, "status should be Completed")
}

func TestScheduler_RecoverStuckTasks_EnqueuesRunningAndPending(t *testing.T) {
	repo := &mockSchedulerRepo{
		runningTasks: []*bt.BacktestTask{
			{ID: uuid.New(), Status: bt.TaskStatusRunning},
		},
		pendingTasks: []*bt.BacktestTask{
			{ID: uuid.New(), Status: bt.TaskStatusPending},
		},
	}
	queue := NewTaskQueue(10)

	s := &BacktestScheduler{
		repo:  repo,
		queue: queue,
		log:   zerolog.Nop(),
	}

	ctx := context.Background()
	s.recoverStuckTasks(ctx)

	assert.Equal(t, bt.TaskStatusPending, repo.statusUpdated,
		"running tasks should be reset to pending")
}
