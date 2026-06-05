package scheduler

import (
	"context"
	"sync"

	"github.com/google/uuid"
)

type RunningBacktestTracker struct {
	mu    sync.RWMutex
	tasks map[uuid.UUID]context.CancelFunc
}

func NewRunningBacktestTracker() *RunningBacktestTracker {
	return &RunningBacktestTracker{
		tasks: make(map[uuid.UUID]context.CancelFunc),
	}
}

func (t *RunningBacktestTracker) Add(taskID uuid.UUID, cancel context.CancelFunc) {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.tasks[taskID] = cancel
}

func (t *RunningBacktestTracker) Remove(taskID uuid.UUID) {
	t.mu.Lock()
	defer t.mu.Unlock()
	delete(t.tasks, taskID)
}

func (t *RunningBacktestTracker) Cancel(taskID uuid.UUID) bool {
	t.mu.RLock()
	cancel, ok := t.tasks[taskID]
	t.mu.RUnlock()
	if ok {
		cancel()
		return true
	}
	return false
}
