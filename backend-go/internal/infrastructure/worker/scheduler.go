package worker

import (
	"context"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/engine"
	"tradex/internal/infrastructure/analysis"
	"tradex/internal/infrastructure/exchange"
	"tradex/internal/domain/indicator"
	"tradex/internal/infrastructure/persistence"
)

type BacktestScheduler struct {
	repo         domain.BacktestRepository
	queue        TaskQueue
	monitor      *ResourceMonitor
	registry     *indicator.Registry
	klineCache   persistence.KlineCache
	klineClient  exchange.KlineClient
	tracker      *RunningBacktestTracker
	analysisStore *analysis.Store
	log          zerolog.Logger
}

type SchedulerConfig struct {
	MaxConcurrency     int
	TaskTimeoutMinutes int
	FeeRate            float64
}

func NewBacktestScheduler(
	repo domain.BacktestRepository,
	queue TaskQueue,
	monitor *ResourceMonitor,
	registry *indicator.Registry,
	klineCache persistence.KlineCache,
	klineClient exchange.KlineClient,
	tracker *RunningBacktestTracker,
	analysisStore *analysis.Store,
	log zerolog.Logger,
) *BacktestScheduler {
	return &BacktestScheduler{
		repo:          repo,
		queue:         queue,
		monitor:       monitor,
		registry:      registry,
		klineCache:    klineCache,
		klineClient:   klineClient,
		tracker:       tracker,
		analysisStore: analysisStore,
		log:           log,
	}
}

func (s *BacktestScheduler) Run(ctx context.Context, cfg SchedulerConfig, guards ...*BacktestWorkerGuard) {
	// acquire PG advisory lock if guard provided
	if len(guards) > 0 && guards[0] != nil {
		if err := guards[0].TryAcquire(ctx); err != nil {
			s.log.Fatal().Err(err).Msg("instance guard: failed to acquire lock")
			return
		}
		defer guards[0].Release(context.Background())
		s.log.Info().Msg("instance guard: advisory lock acquired")
	}

	s.recoverStuckTasks(ctx)

	slots := make(chan struct{}, cfg.MaxConcurrency)
	for i := 0; i < cfg.MaxConcurrency; i++ {
		slots <- struct{}{}
	}

	for {
		select {
		case <-ctx.Done():
			return
		case <-slots:
			go func() {
				defer func() {
					if r := recover(); r != nil {
						s.log.Error().Interface("panic", r).Msg("task goroutine panicked, slot released")
					}
					slots <- struct{}{}
				}()

				if int(s.monitor.AllowedConcurrency()) <= lenRunning(s.tracker) {
					time.Sleep(200 * time.Millisecond)
					return
				}

				taskID, err := s.queue.Dequeue(ctx)
				if err != nil {
					return
				}

				s.executeTask(ctx, taskID, cfg)
			}()
		}
	}
}

func (s *BacktestScheduler) recoverStuckTasks(ctx context.Context) {
	running, err := s.repo.GetRunningTasks(ctx)
	if err != nil {
		s.log.Warn().Err(err).Msg("failed to recover stuck tasks")
		return
	}
	for _, task := range running {
		task.Status = domain.TaskStatusPending
		task.Phase = nil
		s.queue.Enqueue(ctx, task.ID)
		s.log.Info().Str("task_id", task.ID.String()).Msg("recovered stuck task")
	}
}

func (s *BacktestScheduler) executeTask(ctx context.Context, taskID uuid.UUID, cfg SchedulerConfig) {
	taskCtx, cancel := context.WithTimeout(ctx, time.Duration(cfg.TaskTimeoutMinutes)*time.Minute)
	defer cancel()

	s.tracker.Add(taskID, cancel)
	defer s.tracker.Remove(taskID)

	log := s.log.With().Str("task_id", taskID.String()).Logger()

	task, err := s.repo.GetTask(taskCtx, taskID)
	if err != nil {
		log.Error().Err(err).Msg("failed to get task")
		return
	}

	log.Info().Msg("processing task")

	advance := func(phase domain.BacktestPhase) bool {
		return s.repo.UpdateTaskStatus(taskCtx, taskID, domain.TaskStatusRunning, &phase) == nil
	}

	// emit BacktestStarted
	log.Info().Interface("event", domain.BacktestStartedEvent{TaskID: taskID, Timestamp: time.Now()}).Msg("backtest_started")

	advance(domain.PhaseQueued)
	advance(domain.PhaseFetchingData)

	candles, err := s.fetchKlines(taskCtx, task)
	if err != nil {
		log.Error().Err(err).Msg("failed to fetch klines, emitting BacktestFailed")
		log.Info().Interface("event", domain.BacktestFailedEvent{TaskID: taskID, Reason: err.Error(), Timestamp: time.Now()}).Msg("backtest_failed")
		s.repo.UpdateTaskStatus(taskCtx, taskID, domain.TaskStatusFailed, nil)
		return
	}

	log.Info().Int("klines", len(candles)).Msg("fetched klines")
	advance(domain.PhaseRunning)

	strategy, err := s.repo.GetStrategy(taskCtx, task.StrategyID)
	if err != nil {
		log.Error().Err(err).Msg("failed to load strategy")
		s.repo.UpdateTaskStatus(taskCtx, taskID, domain.TaskStatusFailed, nil)
		return
	}

	taskIDStr := taskID.String()
	s.analysisStore.Init(taskIDStr)
	defer s.analysisStore.Remove(taskIDStr)

	feeRate := task.FeeRate
	if feeRate.IsZero() && cfg.FeeRate > 0 {
		feeRate = decimal.NewFromFloat(cfg.FeeRate)
	}

	eng := engine.NewBacktestEngine(s.registry)
	out, err := eng.Run(taskCtx, engine.EngineInput{
		Strategy:       *strategy,
		Pair:           task.Pair,
		Klines:         candles,
		InitialCapital: task.InitialCapital,
		PositionSize:   task.PositionSize,
		FeeRate:        feeRate,
		Timeframe:      task.Timeframe,
		OnAnalysis: func(a domain.BacktestKlineAnalysis) {
			s.analysisStore.Push(taskIDStr, a)
		},
	})

	if err != nil {
		log.Error().Err(err).Msg("engine run failed, emitting BacktestFailed")
		log.Info().Interface("event", domain.BacktestFailedEvent{TaskID: taskID, Reason: err.Error(), Timestamp: time.Now()}).Msg("backtest_failed")
		s.repo.UpdateTaskStatus(taskCtx, taskID, domain.TaskStatusFailed, nil)
		return
	}

	out.Result.StrategyName = strategy.Name

	// 写入守卫：引擎跑完后重读 DB，确认没有被取消
	latest, err := s.repo.GetTask(context.Background(), taskID)
	if err == nil && latest.Status == domain.TaskStatusCancelled {
		log.Warn().Msg("task was cancelled during run, discarding result")
		return
	}

	if err := s.repo.SaveResult(taskCtx, taskID, &out.Result, out.Trades); err != nil {
		log.Error().Err(err).Msg("failed to save result")
		return
	}

	batchSize := 100
	for i := 0; i < len(out.Analysis); i += batchSize {
		end := i + batchSize
		if end > len(out.Analysis) {
			end = len(out.Analysis)
		}
		if err := s.repo.SaveAnalysisBatch(taskCtx, taskID, out.Analysis[i:end]); err != nil {
			log.Error().Err(err).Msg("failed to save analysis batch")
		}
	}

	if err := s.repo.UpdateTaskStatus(taskCtx, taskID, domain.TaskStatusCompleted, nil); err != nil {
		log.Error().Err(err).Msg("failed to update task to completed")
		return
	}

	log.Info().Interface("event",
		domain.BacktestCompletedEvent{
			TaskID:             taskID,
			FinalValue:         out.Result.FinalValue,
			TotalReturnPercent: out.Result.TotalReturnPercent,
			Timestamp:          time.Now(),
		},
	).Msg("backtest_completed")

	log.Info().
		Float64("final_value", f64(out.Result.FinalValue)).
		Int("trades", out.Result.TotalTrades).
		Msg("task completed")
}

func (s *BacktestScheduler) fetchKlines(ctx context.Context, task *domain.BacktestTask) ([]domain.Candle, error) {
	if s.klineCache != nil {
		cached, ok := s.klineCache.Get(ctx, task.Pair, task.Timeframe, task.StartAt, task.EndAt)
		if ok && len(cached) > 0 {
			return cached, nil
		}
	}

	if s.klineClient != nil {
		candles, err := s.klineClient.FetchKlines(ctx, task.Pair, task.Timeframe, task.StartAt, task.EndAt)
		if err == nil {
			if s.klineCache != nil {
				s.klineCache.Set(ctx, task.Pair, task.Timeframe, candles)
			}
			return candles, nil
		}
	}

	return []domain.Candle{}, nil
}

func lenRunning(t *RunningBacktestTracker) int {
	t.mu.RLock()
	defer t.mu.RUnlock()
	return len(t.tasks)
}

func f64(d decimal.Decimal) float64 {
	v, _ := d.Float64()
	return v
}
