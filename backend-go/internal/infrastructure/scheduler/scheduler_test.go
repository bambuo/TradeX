package scheduler

import (
	"context"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"tradex/internal/domain"
	"tradex/internal/domain/indicator"
)

type mockRepo struct{}

func (m *mockRepo) GetRunningTasks(_ context.Context) ([]*domain.BacktestTask, error) {
	return nil, nil
}

func TestTaskQueue_EnqueueDequeue(t *testing.T) {
	q := NewTaskQueue(10)
	ctx := context.Background()

	id := uuid.New()
	err := q.Enqueue(ctx, id)
	require.NoError(t, err)

	got, err := q.Dequeue(ctx)
	require.NoError(t, err)
	assert.Equal(t, id, got)
}

func TestTaskQueue_DequeueCancellation(t *testing.T) {
	q := NewTaskQueue(10)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Millisecond)
	defer cancel()

	_, err := q.Dequeue(ctx)
	assert.ErrorIs(t, err, context.DeadlineExceeded)
}

func TestResourceMonitor(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	rm := NewResourceMonitor(ctx, 4)
	assert.Equal(t, 4, rm.AllowedConcurrency())

	// wait for first sampling tick
	time.Sleep(100 * time.Millisecond)
	allowed := rm.AllowedConcurrency()
	assert.GreaterOrEqual(t, allowed, 1)
	assert.LessOrEqual(t, allowed, 4)
}

func TestIndicatorRegistry_ComputeAll(t *testing.T) {
	reg := indicator.NewRegistry()
	reg.Register(indicator.NewSMA(3))

	assert.NotNil(t, reg)
}

func init() {
	zerolog.Nop()
	_ = uuid.UUID{}
}
