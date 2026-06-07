package worker

import (
	"context"
	"fmt"
	"sync"
	"sync/atomic"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/domain/engine"
	"tradex/internal/domain/indicator"
	"tradex/internal/infra/analysis"
	"tradex/internal/infra/exchange"
	"tradex/internal/infra/persistence"
)

const SafetyNetInterval = 5 * time.Minute

type BacktestScheduler struct {
	repo          domain.BacktestRepository
	queue         TaskQueue
	monitor       *ResourceMonitor
	registry      *indicator.Registry
	klineCache    persistence.KlineCache
	klineClient   exchange.KlineClient
	tracker       *RunningBacktestTracker
	analysisStore *analysis.Store
	log           zerolog.Logger
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
			s.log.Fatal().Err(err).Msg("实例守卫：获取锁失败")
			return
		}
		defer guards[0].Release(context.Background())
		s.log.Info().Msg("实例守卫：已获取 advisory lock")
	}

	go s.recoverStuckTasks(ctx)
	go s.safetyNetLoop(ctx)

	s.log.Info().Int("max_concurrency", cfg.MaxConcurrency).Msg("调度器主循环已启动")

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
						s.log.Error().Interface("panic", r).Msg("任务 goroutine panic，已释放槽位")
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
	runningCount := 0
	if err != nil {
		s.log.Warn().Err(err).Msg("恢复卡死任务失败")
	} else {
		for _, task := range running {
			runningCount++
			if err := s.repo.UpdateTaskStatus(context.Background(), task.ID, domain.TaskStatusPending, nil); err != nil {
				s.log.Warn().Err(err).Str("task_id", task.ID.String()).Msg("重置卡死任务到 DB 失败")
				continue
			}
			s.queue.Enqueue(ctx, task.ID)
			s.log.Info().Str("task_id", task.ID.String()).Msg("已恢复卡死任务")
		}
	}

	pending, err := s.repo.GetPendingTasks(ctx)
	if err != nil {
		s.log.Warn().Err(err).Msg("获取待处理任务失败")
		s.log.Info().Int("running", runningCount).Int("pending", -1).Msg("恢复完成（有错误）")
		return
	}
	pendingCount := 0
	for _, task := range pending {
		pendingCount++
		s.queue.Enqueue(ctx, task.ID)
	}
	s.log.Info().Int("running", runningCount).Int("pending", pendingCount).Msg("恢复完成")
}

func (s *BacktestScheduler) executeTask(ctx context.Context, taskID uuid.UUID, cfg SchedulerConfig) {
	taskCtx, cancel := context.WithTimeout(ctx, time.Duration(cfg.TaskTimeoutMinutes)*time.Minute)
	defer cancel()

	s.tracker.Add(taskID, cancel)
	defer s.tracker.Remove(taskID)

	log := s.log.With().Str("task_id", taskID.String()).Logger()

	task, err := s.repo.GetTask(taskCtx, taskID)
	if err != nil {
		log.Error().Err(err).Msg("获取任务失败")
		return
	}

	log.Info().Msg("任务已出队，开始执行")

	advance := func(from domain.BacktestTaskStatus, phase domain.BacktestPhase) bool {
		ok, _ := s.repo.TryAcquireTask(context.Background(), taskID, from, phase)
		return ok
	}

	log.Info().Interface("event", domain.BacktestStartedEvent{TaskID: taskID, Timestamp: time.Now()}).Msg("回测已开始")

	if !advance(domain.TaskStatusPending, domain.PhaseQueued) {
		log.Warn().Msg("任务状态异常（Queued），已中止")
		return
	}
	if !advance(domain.TaskStatusRunning, domain.PhaseFetchingData) {
		log.Warn().Msg("任务状态异常（FetchingData），已中止")
		return
	}

	candles, err := s.fetchKlines(taskCtx, task)
	if err != nil {
		s.failTask(taskID, err.Error(), log)
		return
	}

	log.Info().Int("klines", len(candles)).Msg("已获取 K 线")
	if !advance(domain.TaskStatusRunning, domain.PhaseRunning) {
		log.Warn().Msg("任务状态异常（Running），已中止")
		return
	}

	strategy, err := s.repo.GetStrategy(taskCtx, task.StrategyID)
	if err != nil {
		s.failTask(taskID, fmt.Sprintf("load strategy: %s", err.Error()), log)
		return
	}

	// 引擎启动前最后检查取消状态
	current, checkErr := s.repo.GetTask(context.Background(), taskID)
	if checkErr == nil && current.Status == domain.TaskStatusCancelled {
		log.Warn().Msg("任务在引擎启动前被取消，已中止")
		return
	}

	taskIDStr := taskID.String()
	s.analysisStore.Init(taskIDStr)
	defer s.analysisStore.Remove(taskIDStr)

	// 恢复去重：查询已存在的分析记录数
	existsCount, _ := s.repo.GetAnalysisCount(taskCtx, taskID)

	// 启动并发刷盘 goroutine（引擎运行期间定期落盘分析数据）
	stopFlush := make(chan struct{})
	var lastFlushed int64 = int64(existsCount)
	var flushWg sync.WaitGroup
	flushWg.Add(1)
	go func() {
		defer flushWg.Done()
		s.batchFlushLoop(taskCtx, taskID, taskIDStr, &lastFlushed, stopFlush)
	}()

	feeRate := decimal.NewFromFloat(cfg.FeeRate)

	eng := engine.NewBacktestEngine(s.registry)
	log.Info().Int("total_klines", len(candles)).Msg("引擎：启动")
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

	// 停止刷盘 goroutine 并等待其完全退出，防止与后续刷入重叠
	close(stopFlush)
	flushWg.Wait()

	if err != nil {
		current, checkErr := s.repo.GetTask(context.Background(), taskID)
		if checkErr == nil && current.Status == domain.TaskStatusCancelled {
			log.Info().Msg("任务在引擎运行中被取消，丢弃结果")
			return
		}
		s.failTask(taskID, err.Error(), log)
		return
	}

	out.Result.StrategyName = strategy.Name
	out.Result.Pair = task.Pair
	out.Result.Timeframe = task.Timeframe
	out.Result.StartAt = task.StartAt
	out.Result.EndAt = task.EndAt
	out.Result.InitialCapital = task.InitialCapital

	// 刷入剩余不足 100 条的分析数据（在事务外，事务失败时分析数据不丢失）
	lf := atomic.LoadInt64(&lastFlushed)
	storeTotal := s.analysisStore.Count(taskIDStr)
	log.Info().Int64("last_flushed", lf).Int("store_total", storeTotal).Msg("分析数据：剩余刷入前")
	remaining, _ := s.analysisStore.ConsumeFrom(taskIDStr, int(lf))
	log.Info().Int("remaining", len(remaining)).Msg("分析数据：待刷入")
	batchSize := 100
	for i := 0; i < len(remaining); i += batchSize {
		end := i + batchSize
		if end > len(remaining) {
			end = len(remaining)
		}
		if err := s.repo.SaveAnalysisBatch(taskCtx, taskID, remaining[i:end]); err != nil {
			log.Error().Err(err).Int("batch_start", i).Int("batch_size", end-i).Msg("刷入剩余分析数据批次失败")
		}
	}

	// 取消守卫：引擎跑完后重读 DB，确认没有被取消
	latest, err := s.repo.GetTask(context.Background(), taskID)
	if err == nil && latest.Status == domain.TaskStatusCancelled {
		log.Warn().Msg("任务在运行中被取消，丢弃结果")
		return
	}

	// 事务：仅结果保存 + 完成状态（分析数据已在外层刷入）
	err = s.repo.ExecuteInTransaction(taskCtx, func(txRepo domain.BacktestRepository) error {
		current, err := txRepo.GetTask(taskCtx, taskID)
		if err != nil {
			return err
		}
		if current.Status == domain.TaskStatusCancelled {
			log.Warn().Msg("任务在事务中被取消，回滚")
			return nil
		}

		if err := txRepo.SaveResult(taskCtx, taskID, &out.Result, out.Trades); err != nil {
			return err
		}

		// 二次校验：SaveResult 后再次检查取消状态
		current2, err2 := txRepo.GetTask(taskCtx, taskID)
		if err2 != nil {
			return err2
		}
		if current2.Status == domain.TaskStatusCancelled {
			log.Warn().Msg("任务在保存结果后被取消，回滚事务")
			return nil
		}

		return txRepo.UpdateTaskStatus(taskCtx, taskID, domain.TaskStatusCompleted, nil)
	})
	if err != nil {
		log.Error().Err(err).Msg("提交结果和完成状态失败，任务可重试")
		return
	}

	log.Info().Interface("event",
		domain.BacktestCompletedEvent{
			TaskID:             taskID,
			FinalValue:         out.Result.FinalValue,
			TotalReturnPercent: out.Result.TotalReturnPercent,
			Timestamp:          time.Now(),
		},
	).Msg("回测已完成")

	log.Info().
		Float64("final_value", f64(out.Result.FinalValue)).
		Int("trades", out.Result.TotalTrades).
		Msg("task completed")
}

func (s *BacktestScheduler) failTask(taskID uuid.UUID, reason string, log zerolog.Logger) {
	log.Error().Str("reason", reason).Msg("回测失败")
	log.Info().Interface("event", domain.BacktestFailedEvent{
		TaskID: taskID, Reason: reason, Timestamp: time.Now(),
	}).Msg("回测失败（事件）")
	if err := s.repo.UpdateTaskStatus(context.Background(), taskID, domain.TaskStatusFailed, nil); err != nil {
		log.Warn().Err(err).Msg("标记任务失败时出错")
	}
}

func (s *BacktestScheduler) safetyNetLoop(ctx context.Context) {
	s.log.Info().Msg("安全网：首次扫表")
	s.drainPending(ctx)
	ticker := time.NewTicker(SafetyNetInterval)
	defer ticker.Stop()
	s.log.Info().Dur("interval", SafetyNetInterval).Msg("安全网：循环已启动")
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			s.drainPending(ctx)
		}
	}
}

func (s *BacktestScheduler) drainPending(ctx context.Context) {
	tasks, err := s.repo.GetPendingTasks(ctx)
	if err != nil {
		s.log.Warn().Err(err).Msg("安全网扫表失败")
		return
	}
	for _, task := range tasks {
		s.queue.Enqueue(ctx, task.ID)
	}
	s.log.Info().Int("found", len(tasks)).Msg("安全网：扫表完成")
}

func (s *BacktestScheduler) batchFlushLoop(ctx context.Context, taskID uuid.UUID, taskIDStr string, lastFlushed *int64, stop <-chan struct{}) {
	ticker := time.NewTicker(1 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-stop:
			return
		case <-ticker.C:
			lf := atomic.LoadInt64(lastFlushed)
			batch, total := s.analysisStore.ConsumeFrom(taskIDStr, int(lf))
			if len(batch) >= 100 {
				if err := s.repo.SaveAnalysisBatch(ctx, taskID, batch); err != nil {
					s.log.Error().Err(err).Str("task_id", taskID.String()).Msg("批量刷入分析数据失败")
					continue
				}
				atomic.StoreInt64(lastFlushed, int64(total))
			}
		}
	}
}

func (s *BacktestScheduler) fetchKlines(ctx context.Context, task *domain.BacktestTask) ([]domain.Candle, error) {
	if s.klineCache != nil {
		cached, ok := s.klineCache.Get(ctx, task.ExchangeID, task.Pair, task.Timeframe, task.StartAt, task.EndAt)
		if ok && len(cached) > 0 {
			return cached, nil
		}
	}

	if s.klineClient != nil {
		candles, err := s.klineClient.FetchKlines(ctx, task.Pair, task.Timeframe, task.StartAt, task.EndAt)
		if err == nil {
			if s.klineCache != nil {
				s.klineCache.Set(ctx, task.ExchangeID, task.Pair, task.Timeframe, candles)
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
