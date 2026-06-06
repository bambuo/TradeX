package scheduler

import (
	"context"
	"database/sql"
	"fmt"
)

// BacktestWorkerGuard uses a PostgreSQL advisory lock to ensure
// only one backtest worker instance runs at a time.
type BacktestWorkerGuard struct {
	db     *sql.DB
	lockID int64
}

func NewBacktestWorkerGuard(db *sql.DB) *BacktestWorkerGuard {
	return &BacktestWorkerGuard{
		db:     db,
		lockID: 0x4241434B54455354, // "BACKTEST" as int64
	}
}

func (g *BacktestWorkerGuard) TryAcquire(ctx context.Context) error {
	var acquired bool
	err := g.db.QueryRowContext(ctx, "SELECT pg_try_advisory_lock($1)", g.lockID).Scan(&acquired)
	if err != nil {
		return fmt.Errorf("pg_try_advisory_lock: %w", err)
	}
	if !acquired {
		return fmt.Errorf("another backtest worker instance is already running")
	}
	return nil
}

func (g *BacktestWorkerGuard) Release(ctx context.Context) error {
	var released bool
	err := g.db.QueryRowContext(ctx, "SELECT pg_advisory_unlock($1)", g.lockID).Scan(&released)
	if err != nil {
		return fmt.Errorf("pg_advisory_unlock: %w", err)
	}
	return nil
}
