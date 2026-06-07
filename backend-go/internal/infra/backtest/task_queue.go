package backtest

import (
	"context"
	"sync"

	"github.com/google/uuid"
)

type TaskQueue interface {
	Enqueue(ctx context.Context, taskID uuid.UUID) error
	Dequeue(ctx context.Context) (uuid.UUID, error)
	Close()
}

type chanTaskQueue struct {
	ch     chan uuid.UUID
	closed bool
	mu     sync.RWMutex
	once   sync.Once
}

func NewTaskQueue(bufferSize int) TaskQueue {
	return &chanTaskQueue{ch: make(chan uuid.UUID, bufferSize)}
}

func (q *chanTaskQueue) Enqueue(ctx context.Context, taskID uuid.UUID) error {
	q.mu.RLock()
	closed := q.closed
	q.mu.RUnlock()
	if closed {
		return nil
	}
	select {
	case <-ctx.Done():
		return ctx.Err()
	case q.ch <- taskID:
		return nil
	}
}

func (q *chanTaskQueue) Dequeue(ctx context.Context) (uuid.UUID, error) {
	select {
	case <-ctx.Done():
		return uuid.UUID{}, ctx.Err()
	case id := <-q.ch:
		return id, nil
	}
}

func (q *chanTaskQueue) Close() {
	q.once.Do(func() {
		q.mu.Lock()
		q.closed = true
		q.mu.Unlock()
		close(q.ch)
	})
}
