package handler

import (
	"encoding/json"
	"fmt"
	"strconv"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"

	bt "tradex/internal/domain/backtest"
)

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
		writeSSE(c, flusher, "data: {\"type\":\"status\",\"status\":\"NotFound\"}\n\n")
		return
	}

	if task.Status != bt.TaskStatusCompleted && h.analysisStore.Exists(taskIDStr) {
		streamIncremental(c, h, taskIDStr, task.Status, flusher, delayMs)
		return
	}

	if task.Status == bt.TaskStatusCompleted {
		streamCompleted(c, h, id, flusher, delayMs)
		return
	}

	writeSSE(c, flusher, fmt.Sprintf("data: {\"type\":\"status\",\"status\":%q}\n\n", task.Status))
	writeSSE(c, flusher, "data: {\"type\":\"complete\"}\n\n")
}

func streamIncremental(c *gin.Context, h *BacktestHandler, taskIDStr string, status bt.BacktestTaskStatus, flusher interface{ Flush() }, delayMs int) {
	writeSSE(c, flusher, fmt.Sprintf("data: {\"type\":\"status\",\"status\":%q,\"incremental\":true}\n\n", status))

	existing := h.analysisStore.Get(taskIDStr)
	for _, item := range existing {
		if ctxDone(c) {
			return
		}
		writeAnalysisItem(c, item, flusher)
	}

	total := len(existing)
	writeSSE(c, flusher, fmt.Sprintf("data: {\"type\":\"meta\",\"total\":%d,\"incremental\":true}\n\n", total))

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
				writeAnalysisItem(c, item, flusher)
			}
		}
	}
	writeSSE(c, flusher, "data: {\"type\":\"complete\"}\n\n")
}

func streamCompleted(c *gin.Context, h *BacktestHandler, id uuid.UUID, flusher interface{ Flush() }, delayMs int) {
	analysis, err := h.svc.GetAnalysis(c.Request.Context(), id, 0, 100000)
	if err != nil || len(analysis) == 0 {
		writeSSE(c, flusher, "data: {\"type\":\"complete\"}\n\n")
		return
	}

	writeSSE(c, flusher, fmt.Sprintf("data: {\"type\":\"meta\",\"total\":%d}\n\n", len(analysis)))
	for _, item := range analysis {
		if ctxDone(c) {
			return
		}
		if delayMs > 0 {
			select {
			case <-c.Request.Context().Done():
				return
			case <-time.After(time.Duration(delayMs) * time.Millisecond):
			}
		}
		writeAnalysisItem(c, item, flusher)
	}
	writeSSE(c, flusher, "data: {\"type\":\"complete\"}\n\n")
}

func writeAnalysisItem(c *gin.Context, a bt.BacktestKlineAnalysis, flusher interface{ Flush() }) {
	payload := struct {
		Type       string             `json:"type"`
		Index      int                `json:"index"`
		Timestamp  string             `json:"timestamp"`
		Open       json.Number        `json:"open"`
		High       json.Number        `json:"high"`
		Low        json.Number        `json:"low"`
		Close      json.Number        `json:"close"`
		Volume     json.Number        `json:"volume"`
		InPosition bool               `json:"inPosition"`
		Action     string             `json:"action"`
		Indicators map[string]float64 `json:"indicators,omitempty"`
	}{
		Type:       "item",
		Index:      a.Index,
		Timestamp:  a.Timestamp.Format(time.RFC3339),
		Open:       json.Number(a.Open.String()),
		High:       json.Number(a.High.String()),
		Low:        json.Number(a.Low.String()),
		Close:      json.Number(a.Close.String()),
		Volume:     json.Number(a.Volume.String()),
		InPosition: a.InPosition,
		Action:     a.Action,
		Indicators: a.IndicatorValues,
	}
	b, _ := json.Marshal(payload)
	c.Writer.WriteString(fmt.Sprintf("data: %s\n\n", string(b)))
}

func writeSSE(c *gin.Context, flusher interface{ Flush() }, data string) {
	c.Writer.WriteString(data)
	if flusher != nil {
		flusher.Flush()
	}
}

func ctxDone(c *gin.Context) bool {
	select {
	case <-c.Request.Context().Done():
		return true
	default:
		return false
	}
}
