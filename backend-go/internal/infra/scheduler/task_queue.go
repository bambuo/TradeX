package scheduler

import (
	"context"

	"github.com/google/uuid"
)

type TaskQueue interface {
	Enqueue(ctx context.Context, taskID uuid.UUID) error
	Dequeue(ctx context.Context) (uuid.UUID, error)
	Close()
}

type chanTaskQueue struct {
	ch chan uuid.UUID
}

func NewTaskQueue(bufferSize int) TaskQueue {
	return &chanTaskQueue{ch: make(chan uuid.UUID, bufferSize)}
}

func (q *chanTaskQueue) Enqueue(_ context.Context, taskID uuid.UUID) error {
	q.ch <- taskID
	return nil
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
	close(q.ch)
}
