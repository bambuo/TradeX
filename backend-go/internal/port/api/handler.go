package api

import (
	"errors"
	"runtime"
	"strconv"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/rs/zerolog"

	"github.com/tradex/backend-go/internal/domain"
	"github.com/tradex/backend-go/internal/app"
)

type BacktestHandler struct {
	svc *service.BacktestService
	log zerolog.Logger
}

func NewBacktestHandler(svc *service.BacktestService, log zerolog.Logger) *BacktestHandler {
	return &BacktestHandler{svc: svc, log: log}
}

func (h *BacktestHandler) RegisterRoutes(r *gin.Engine) {
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

	svcReq := service.CreateBacktestRequest{
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
		h.log.Error().Err(err).Msg("failed to create task")
		InternalError(c, "failed to create task")
		return
	}

	Created(c, gin.H{
		"id":     task.ID,
		"status": task.Status,
	})
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
		h.log.Error().Err(err).Msg("failed to list tasks")
		InternalError(c, "failed to list tasks")
		return
	}

	Success(c, gin.H{"items": tasks, "total": total, "page": page})
}

func (h *BacktestHandler) GetTask(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "invalid id")
		return
	}

	task, err := h.svc.GetTask(c.Request.Context(), id)
	if err != nil {
		NotFound(c, "task not found")
		return
	}

	Success(c, task)
}

func (h *BacktestHandler) CancelTask(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "invalid id")
		return
	}

	if err := h.svc.CancelTask(c.Request.Context(), id); err != nil {
		if errors.Is(err, domain.ErrNotFound) {
			NotFound(c, "task not found")
			return
		}
		if errors.Is(err, domain.ErrConflict) {
			Conflict(c, "task already finished")
			return
		}
		h.log.Error().Err(err).Msg("failed to cancel task")
		InternalError(c, "failed to cancel task")
		return
	}

	Success(c, gin.H{"id": id, "status": domain.TaskStatusCancelled})
}

func (h *BacktestHandler) GetResult(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "invalid id")
		return
	}

	task, err := h.svc.GetTask(c.Request.Context(), id)
	if err != nil {
		NotFound(c, "task not found")
		return
	}

	if task.Status != domain.TaskStatusCompleted {
		Conflict(c, "task not completed yet")
		return
	}

	result, trades, err := h.svc.GetResult(c.Request.Context(), id)
	if err != nil {
		NotFound(c, "result not found")
		return
	}

	Success(c, gin.H{"result": result, "trades": trades})
}

func (h *BacktestHandler) GetAnalysis(c *gin.Context) {
	id, err := uuid.Parse(c.Param("id"))
	if err != nil {
		BadRequest(c, "invalid id")
		return
	}

	cursor, _ := strconv.Atoi(c.DefaultQuery("cursor", "0"))
	limit, _ := strconv.Atoi(c.DefaultQuery("limit", "100"))
	if limit > 1000 {
		limit = 1000
	}

	analysis, err := h.svc.GetAnalysis(c.Request.Context(), id, cursor, limit)
	if err != nil {
		Success(c, []any{})
		return
	}

	Success(c, analysis)
}
