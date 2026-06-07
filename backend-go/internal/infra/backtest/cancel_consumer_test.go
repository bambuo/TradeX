package backtest

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
)

func TestBacktestCancellationConsumer_CancelRunningTask(t *testing.T) {
	tracker := NewRunningBacktestTracker()
	taskID := uuid.New()

	ctx, cancel := context.WithCancel(context.Background())
	tracker.Add(taskID, cancel)

	// Verify task is tracked
	assert.True(t, tracker.Cancel(taskID), "should cancel running task")

	// Verify context is cancelled
	select {
	case <-ctx.Done():
		// expected
	default:
		t.Error("context should be cancelled after Cancel")
	}
}

func TestBacktestCancellationConsumer_CancelNonExistentTask_ReturnsFalse(t *testing.T) {
	tracker := NewRunningBacktestTracker()
	taskID := uuid.New()

	assert.False(t, tracker.Cancel(taskID), "should return false for non-existent task")
}

func TestBacktestCancellationConsumer_CancelAndRemove_Consistent(t *testing.T) {
	tracker := NewRunningBacktestTracker()
	taskID := uuid.New()

	ctx, cancel := context.WithCancel(context.Background())
	tracker.Add(taskID, cancel)

	// Cancel calls the cancel func but doesn't remove from tracker
	assert.True(t, tracker.Cancel(taskID), "first cancel should succeed")

	// Second cancel should also succeed (Cancel doesn't delete)
	assert.True(t, tracker.Cancel(taskID), "second cancel should succeed (cancel is idempotent)")

	// After Remove, Cancel returns false
	tracker.Remove(taskID)
	assert.False(t, tracker.Cancel(taskID), "should return false after Remove")

	// Context should be cancelled
	select {
	case <-ctx.Done():
		// expected
	default:
		t.Error("context should be cancelled")
	}
}

func TestRunningBacktestTracker_AddAndRemove(t *testing.T) {
	tracker := NewRunningBacktestTracker()
	taskID := uuid.New()

	tracker.Add(taskID, func() {})

	assert.True(t, tracker.Cancel(taskID), "should cancel after add")
	tracker.Remove(taskID)
	assert.False(t, tracker.Cancel(taskID), "should not cancel after remove")
}
