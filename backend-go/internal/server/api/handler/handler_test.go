package handler

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"tradex/internal/domain"
	"tradex/internal/server/app"
)

var errMock = errors.New("mock error")

type mockRepo struct{}

func (m *mockRepo) CreateTask(_ context.Context, _ *domain.BacktestTask) error {
	return nil
}
func (m *mockRepo) GetTask(_ context.Context, _ uuid.UUID) (*domain.BacktestTask, error) {
	return nil, errMock
}
func (m *mockRepo) UpdateTaskStatus(_ context.Context, _ uuid.UUID, _ domain.BacktestTaskStatus, _ *domain.BacktestPhase) error {
	return nil
}
func (m *mockRepo) UpdateTaskProgress(_ context.Context, _ uuid.UUID, _ int) error {
	return nil
}
func (m *mockRepo) ListTasks(_ context.Context, _ domain.TaskFilter) ([]*domain.BacktestTask, int, error) {
	return nil, 0, nil
}
func (m *mockRepo) SaveResult(_ context.Context, _ uuid.UUID, _ *domain.BacktestResult, _ []domain.BacktestTrade) error {
	return nil
}
func (m *mockRepo) GetResult(_ context.Context, _ uuid.UUID) (*domain.BacktestResult, []domain.BacktestTrade, error) {
	return nil, nil, errMock
}
func (m *mockRepo) SaveAnalysisBatch(_ context.Context, _ uuid.UUID, _ []domain.BacktestKlineAnalysis) error {
	return nil
}
func (m *mockRepo) GetAnalysis(_ context.Context, _ uuid.UUID, _ int, _ int) ([]domain.BacktestKlineAnalysis, error) {
	return nil, nil
}
func (m *mockRepo) GetPendingTasks(_ context.Context) ([]*domain.BacktestTask, error) {
	return nil, nil
}
func (m *mockRepo) GetRunningTasks(_ context.Context) ([]*domain.BacktestTask, error) {
	return nil, nil
}
func (m *mockRepo) GetAnalysisCount(_ context.Context, _ uuid.UUID) (int, error) {
	return 0, nil
}
func (m *mockRepo) GetStrategy(_ context.Context, _ uuid.UUID) (*domain.Strategy, error) {
	return nil, errMock
}
func (m *mockRepo) ExecuteInTransaction(_ context.Context, fn func(domain.BacktestRepository) error) error {
	return fn(m)
}
func (m *mockRepo) TryAcquireTask(_ context.Context, _ uuid.UUID, _ domain.BacktestTaskStatus, _ domain.BacktestPhase) (bool, error) {
	return true, nil
}

func newTestHandler() *BacktestHandler {
	return NewBacktestHandler(app.NewBacktestService(&mockRepo{}), zerolog.Nop())
}

func TestHandler_Health(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	r.Use(RecoveryMiddleware(zerolog.Nop()))
	h.RegisterRoutes(r)

	w := httptest.NewRecorder()
	req, _ := http.NewRequest("GET", "/health", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 200, w.Code)
	assert.Contains(t, w.Body.String(), `"status":"ok"`)
}

func TestHandler_CreateTask_Validation(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	tests := []struct {
		name string
		body string
		code int
	}{
		{"empty body", `{}`, 400},
		{"missing strategy_id", `{"exchange_id":"binance","pair":"BTCUSDT","timeframe":"1h","initial_capital":1000,"start_at":"2024-01-01T00:00:00Z","end_at":"2024-01-02T00:00:00Z"}`, 400},
		{"invalid capital", `{"strategy_id":"00000000-0000-0000-0000-000000000001","exchange_id":"binance","pair":"BTCUSDT","timeframe":"1h","initial_capital":0,"start_at":"2024-01-01T00:00:00Z","end_at":"2024-01-02T00:00:00Z"}`, 400},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			w := httptest.NewRecorder()
			req, _ := http.NewRequest("POST", "/api/v1/backtest",
				bytes.NewReader([]byte(tt.body)))
			req.Header.Set("Content-Type", "application/json")
			r.ServeHTTP(w, req)

			assert.Equal(t, tt.code, w.Code)

			var resp Response
			err := json.Unmarshal(w.Body.Bytes(), &resp)
			require.NoError(t, err)
			assert.NotEqual(t, 0, resp.Code)
		})
	}
}

func TestHandler_CancelTask_InvalidID(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	w := httptest.NewRecorder()
	req, _ := http.NewRequest("POST", "/api/v1/backtest/not-a-uuid/cancel", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 400, w.Code)
}

func TestHandler_GetAnalysis_InvalidID(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	w := httptest.NewRecorder()
	req, _ := http.NewRequest("GET", "/api/v1/backtest/invalid/analysis", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 400, w.Code)
}

func TestHandler_GetResult_TaskNotFound(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	taskID := uuid.New()
	w := httptest.NewRecorder()
	req, _ := http.NewRequest("GET", "/api/v1/backtest/"+taskID.String()+"/result", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 404, w.Code)
}

func TestHandler_ListTasks_ReturnsJSON(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	w := httptest.NewRecorder()
	req, _ := http.NewRequest("GET", "/api/v1/backtest?page=1&page_size=10", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 200, w.Code)

	var resp Response
	err := json.Unmarshal(w.Body.Bytes(), &resp)
	require.NoError(t, err)
	assert.Equal(t, 0, resp.Code)
}

func TestHandler_GetAnalysisCount_InvalidID(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	w := httptest.NewRecorder()
	req, _ := http.NewRequest("GET", "/api/v1/backtest/invalid/analysis/count", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 400, w.Code)
}

func TestHandler_Livez_Healthy(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	w := httptest.NewRecorder()
	req, _ := http.NewRequest("GET", "/livez", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 200, w.Code)
	assert.Contains(t, w.Body.String(), `"alive"`)
}

func TestHandler_Readyz_Ready(t *testing.T) {
	h := newTestHandler()
	r := gin.New()
	h.RegisterRoutes(r)

	w := httptest.NewRecorder()
	req, _ := http.NewRequest("GET", "/readyz", nil)
	r.ServeHTTP(w, req)

	assert.Equal(t, 200, w.Code)
	assert.Contains(t, w.Body.String(), `"ready"`)
}
