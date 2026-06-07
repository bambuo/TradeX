package handler

import (
	"errors"
	"fmt"
	"runtime"
	"strconv"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/rs/zerolog"

	"tradex/internal/domain"
	"tradex/internal/server/app"
)

const CancelStreamKey = "tradex:backtest:cancel"

type BacktestHandler struct {
	svc           *app.BacktestService
	cancelPub     domain.CancelNotifier
	taskNotif     domain.TaskNotifier
	analysisStore domain.AnalysisStore
	log           zerolog.Logger
}

func NewBacktestHandler(svc *app.BacktestService, log zerolog.Logger) *BacktestHandler {
	return &BacktestHandler{svc: svc, log: log}
}

func (h *BacktestHandler) WithCancelPublisher(pub domain.CancelNotifier) *BacktestHandler {
	h.cancelPub = pub
	return h
}

func (h *BacktestHandler) WithTaskNotifier(notif domain.TaskNotifier) *BacktestHandler {
	h.taskNotif = notif
	return h
}

func (h *BacktestHandler) WithAnalysisStore(store domain.AnalysisStore) *BacktestHandler {
	h.analysisStore = store
	return h
}

func (h *BacktestHandler) RegisterRoutes(r *gin.Engine) {
	r.GET("/livez", func(c *gin.Context) {
		c.JSON(200, gin.H{"status": "alive"})
	})
	r.GET("/readyz", func(c *gin.Context) {
		c.JSON(200, gin.H{"status": "ready"})
	})
	r.GET("/health", func(c *gin.Context) {
		var m runtime.MemStats
		runtime.ReadMemStats(&m)
		c.JSON(200, gin.H{
			"status": "ok",
			"memory": gin.H{
				"alloc_mb":       m.Alloc / 1024 / 1024,
				"total_alloc_mb": m.TotalAlloc / 1024 / 1024,
				"sys_mb":         m.Sys / 1024 / 1024,
				"goroutines":     runtime.NumGoroutine(),
			},
		})
	})

	v1 := r.Group("/api/v1")
	{
		v1.POST("/backtest", h.CreateTask)
		v1.GET("/backtest", h.ListTasks)
		v1.GET("/backtest/:id", h.GetTask)
		v1.GET("/backtest/:id/result", h.GetResult)
		v1.GET("/backtest/:id/analysis", h.GetAnalysis)
		v1.POST("/backtest/:id/cancel", h.CancelTask)
		v1.GET("/backtest/:id/analysis/count", h.GetAnalysisCount)
		v1.GET("/backtest/:id/analysis/stream", h.StreamAnalysis)
	}
}

type createBacktestRequest struct {
	StrategyID     string   `json:"strategy_id" binding:"required"`
	ExchangeID     string   `json:"exchange_id" binding:"required"`
	Pair           string   `json:"pair" binding:"required"`
	Timeframe      string   `json:"timeframe" binding:"required"`
	InitialCapital float64  `json:"initial_capital" binding:"required,gt=0"`
	PositionSize   *float64 `json:"position_size"`
	StartAt        string   `json:"start_at" binding:"required"`
	EndAt          string   `json:"end_at" binding:"required"`
	FeeRate        float64  `json:"fee_rate"`
}

func (h *BacktestHandler) CreateTask(c *gin.Context) {
	var req createBacktestRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		BadRequest(c, err.Error())
		return
	}

	svcReq := app.CreateBacktestRequest{
		StrategyID:     req.StrategyID,
		ExchangeID:     req.ExchangeID,
		Pair:           req.Pair,
		Timeframe:      req.Timeframe,
		InitialCapital: req.InitialCapital,
		PositionSize:   req.PositionSize,
		StartAt:        req.StartAt,
		EndAt:          req.EndAt,
		FeeRate:        req.FeeRate,
	}

	task, err := h.svc.CreateTask(c.Request.Context(), svcReq)
	if err != nil {
		if errors.Is(err, domain.ErrInvalidInput) {
			BadRequest(c, err.Error())
			return
		}
		h.log.Error().Err(err).Msg("创建任务失败")
		InternalError(c, "创建任务失败")
		return
	}

	Created(c, gin.H{
		"id":     task.ID,
		"status": task.Status,
	})

	// 通知 Worker 有新任务待处理
	if h.taskNotif != nil {
		if err := h.taskNotif.NotifyCreate(c.Request.Context(), task.ID.String()); err != nil {
			h.log.Warn().Err(err).Str("task_id", task.ID.String()).Msg("发布任务创建事件失败")
		}
	}
}

func (h *BacktestHandler) ListTasks(c *gin.Context) {
	page, _ := strconv.Atoi(c.DefaultQuery("page", "1"))
	pageSize, _ := strconv.Atoi(c.DefaultQuery("page_size", "20"))

	var filter domain.TaskFilter
	if s := c.Query("status"); s != "" {
		status := domain.BacktestTaskStatus(s)
		filter.Status = &status
	}
	if p := c.Query("pair"); p != "" {
		filter.Pair = &p
	}
	filter.Page = page
	filter.PageSize = pageSize

	tasks, total, err := h.svc.ListTasks(c.Request.Context(), filter)
	if err != nil {
		h.log.Error().Err(err).Msg("查询任务列表失败")
		InternalError(c, "查询任务列表失败")
		return
	}

	Success(c, gin.H{"items": tasks, "total": total, "page": page})
}

func (h *BacktestHandler) GetTask(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "无效的 ID")
		return
	}

	task, err := h.svc.GetTask(c.Request.Context(), id)
	if err != nil {
		NotFound(c, "任务不存在")
		return
	}

	Success(c, task)
}

func (h *BacktestHandler) CancelTask(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "无效的 ID")
		return
	}

	if err := h.svc.CancelTask(c.Request.Context(), id); err != nil {
		if errors.Is(err, domain.ErrNotFound) {
			NotFound(c, "任务不存在")
			return
		}
		if errors.Is(err, domain.ErrConflict) {
			Conflict(c, "任务已结束，无法取消")
			return
		}
		h.log.Error().Err(err).Msg("取消失败")
		InternalError(c, "取消失败")
		return
	}

	// publish cancel event to Redis Stream for cross-process notification
	if h.cancelPub != nil {
		if err := h.cancelPub.NotifyCancel(c.Request.Context(), id.String()); err != nil {
			h.log.Warn().Err(err).Str("task_id", id.String()).Msg("发布取消事件失败")
		} else {
			h.log.Info().Str("task_id", id.String()).Msg("取消事件已发布到流")
		}
	}

	Success(c, gin.H{"id": id, "status": domain.TaskStatusCancelled})
}

func (h *BacktestHandler) GetResult(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "无效的 ID")
		return
	}

	task, err := h.svc.GetTask(c.Request.Context(), id)
	if err != nil {
		NotFound(c, "任务不存在")
		return
	}

	if task.Status != domain.TaskStatusCompleted {
		Conflict(c, "任务尚未完成")
		return
	}

	result, trades, err := h.svc.GetResult(c.Request.Context(), id)
	if err != nil {
		NotFound(c, "结果不存在")
		return
	}

	Success(c, gin.H{"result": result, "trades": trades})
}

func (h *BacktestHandler) StreamAnalysis(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "无效的 ID")
		return
	}

	speedStr := c.DefaultQuery("speed", "1")
	speed, _ := strconv.Atoi(speedStr)
	if speed < 1 {
		speed = 1
	}
	if speed > 50 {
		speed = 50
	}
	delayMs := 300 / speed

	taskIDStr := id.String()
	c.Writer.Header().Set("Content-Type", "text/event-stream")
	c.Writer.Header().Set("Cache-Control", "no-cache")
	c.Writer.Header().Set("Connection", "keep-alive")
	c.Writer.Header().Set("X-Accel-Buffering", "no")

	flusher, _ := c.Writer.(interface{ Flush() })

	task, err := h.svc.GetTask(c.Request.Context(), id)
	if err != nil {
		c.Writer.WriteString("data: {\"type\":\"status\",\"status\":\"NotFound\"}\n\n")
		if flusher != nil {
			flusher.Flush()
		}
		return
	}

	// 进行中的任务：从 analysisStore 实时流式推送
	if task.Status != domain.TaskStatusCompleted && h.analysisStore.Exists(taskIDStr) {
		c.Writer.WriteString(fmt.Sprintf("data: {\"type\":\"status\",\"status\":%q,\"incremental\":true}\n\n", task.Status))
		if flusher != nil {
			flusher.Flush()
		}

		// 先发送所有已存在的分析数据
		existing := h.analysisStore.Get(taskIDStr)
		for _, item := range existing {
			select {
			case <-c.Request.Context().Done():
				return
			default:
			}
			writeAnalysisSSE(c, item, flusher)
			if flusher != nil {
				flusher.Flush()
			}
		}

		// 再订阅增量推送
		total := len(existing)
		c.Writer.WriteString(fmt.Sprintf("data: {\"type\":\"meta\",\"total\":%d,\"incremental\":true}\n\n", total))
		if flusher != nil {
			flusher.Flush()
		}

		ch, ok := h.analysisStore.Subscribe(taskIDStr)
		if ok {
		loop:
			for {
				select {
				case <-c.Request.Context().Done():
					break loop
				case item, ok := <-ch:
					if !ok {
						break loop
					}
					if delayMs > 0 {
						select {
						case <-c.Request.Context().Done():
							break loop
						case <-time.After(time.Duration(delayMs) * time.Millisecond):
						}
					}
					writeAnalysisSSE(c, item, flusher)
					if flusher != nil {
						flusher.Flush()
					}
				}
			}
		}
		c.Writer.WriteString("data: {\"type\":\"complete\"}\n\n")
		if flusher != nil {
			flusher.Flush()
		}
		return
	}

	// 已完成的任务：从 DB 读取后流式推送
	if task.Status == domain.TaskStatusCompleted {
		analysis, err := h.svc.GetAnalysis(c.Request.Context(), id, 0, 100000)
		if err != nil || len(analysis) == 0 {
			c.Writer.WriteString("data: {\"type\":\"complete\"}\n\n")
			if flusher != nil {
				flusher.Flush()
			}
			return
		}
		c.Writer.WriteString(fmt.Sprintf("data: {\"type\":\"meta\",\"total\":%d}\n\n", len(analysis)))
		if flusher != nil {
			flusher.Flush()
		}
		for _, item := range analysis {
			select {
			case <-c.Request.Context().Done():
				return
			default:
			}
			if delayMs > 0 {
				select {
				case <-c.Request.Context().Done():
					return
				case <-time.After(time.Duration(delayMs) * time.Millisecond):
				}
			}
			writeAnalysisSSE(c, item, flusher)
			if flusher != nil {
				flusher.Flush()
			}
		}
		c.Writer.WriteString("data: {\"type\":\"complete\"}\n\n")
		if flusher != nil {
			flusher.Flush()
		}
		return
	}

	// 未开始或未知状态
	c.Writer.WriteString(fmt.Sprintf("data: {\"type\":\"status\",\"status\":%q}\n\n", task.Status))
	if flusher != nil {
		flusher.Flush()
	}
	c.Writer.WriteString("data: {\"type\":\"complete\"}\n\n")
	if flusher != nil {
		flusher.Flush()
	}
}

func writeAnalysisSSE(c *gin.Context, a domain.BacktestKlineAnalysis, flusher interface{ Flush() }) {
	entry := ""
	entry += fmt.Sprintf(`"index":%d,`, a.KlineIndex)
	entry += fmt.Sprintf(`"timestamp":%q,`, a.Timestamp.Format(time.RFC3339))
	open, _ := a.Open.Float64()
	high, _ := a.High.Float64()
	low, _ := a.Low.Float64()
	close_, _ := a.Close.Float64()
	volume, _ := a.Volume.Float64()
	entry += fmt.Sprintf(`"open":%f,"high":%f,"low":%f,"close":%f,"volume":%f,`, open, high, low, close_, volume)
	entry += fmt.Sprintf(`"inPosition":%v,`, a.InPosition)
	entry += fmt.Sprintf(`"action":%q`, a.Action)

	if a.IndicatorValues != nil {
		entry += `,"indicators":{`
		first := true
		for k, v := range a.IndicatorValues {
			if !first {
				entry += ","
			}
			entry += fmt.Sprintf("%q:%f", k, v)
			first = false
		}
		entry += "}"
	}

	payload := fmt.Sprintf("data: {\"type\":\"item\",%s}\n\n", entry)
	c.Writer.WriteString(payload)
}

func (h *BacktestHandler) GetAnalysisCount(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "无效的 ID")
		return
	}

	count, err := h.svc.GetAnalysisCount(c.Request.Context(), id)
	if err != nil {
		h.log.Error().Err(err).Str("task_id", id.String()).Msg("查询分析数量失败")
		InternalError(c, "查询分析数量失败")
		return
	}

	Success(c, gin.H{"count": count})
}

func (h *BacktestHandler) GetAnalysis(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "无效的 ID")
		return
	}

	cursor, _ := strconv.Atoi(c.DefaultQuery("cursor", "0"))
	limit, _ := strconv.Atoi(c.DefaultQuery("limit", "100"))
	if limit > 1000 {
		limit = 1000
	}

	analysis, err := h.svc.GetAnalysis(c.Request.Context(), id, cursor, limit)
	if err != nil {
		h.log.Error().Err(err).Str("task_id", id.String()).Msg("查询分析数据失败")
		InternalError(c, "查询分析数据失败")
		return
	}

	Success(c, analysis)
}
