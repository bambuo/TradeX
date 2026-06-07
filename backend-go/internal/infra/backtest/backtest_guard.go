package backtest

import (
	"context"
	"database/sql"
	"fmt"
)

const BacktestWorkerLockID int64 = 0x4241434B54455354

type BacktestWorkerGuard struct {
	db     *sql.DB
	conn   *sql.Conn
	lockID int64
	held   bool
}

func NewBacktestWorkerGuard(db *sql.DB) *BacktestWorkerGuard {
	return &BacktestWorkerGuard{db: db, lockID: BacktestWorkerLockID}
}

func (g *BacktestWorkerGuard) TryAcquire(ctx context.Context) error {
	conn, err := g.db.Conn(ctx)
	if err != nil {
		return fmt.Errorf("backtest guard: get dedicated connection: %w", err)
	}
	var acquired bool
	if err := conn.QueryRowContext(ctx, "SELECT pg_try_advisory_lock($1)", g.lockID).Scan(&acquired); err != nil {
		conn.Close()
		return fmt.Errorf("backtest guard: pg_try_advisory_lock: %w", err)
	}
	if !acquired {
		conn.Close()
		return fmt.Errorf("another backtest worker instance is already running")
	}
	g.conn = conn
	g.held = true
	return nil
}

func (g *BacktestWorkerGuard) Release(ctx context.Context) error {
	if !g.held || g.conn == nil {
		return nil
	}
	g.held = false
	conn := g.conn
	g.conn = nil
	var released bool
	if err := conn.QueryRowContext(ctx, "SELECT pg_advisory_unlock($1)", g.lockID).Scan(&released); err != nil {
		conn.Close()
		return fmt.Errorf("backtest guard: pg_advisory_unlock: %w", err)
	}
	return conn.Close()
}
