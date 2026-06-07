package guard

import (
	"context"
	"database/sql"
	"fmt"
)

const LiveWorkerLockID int64 = 0x5452414445585F01

type SingleInstanceGuard struct {
	db     *sql.DB
	conn   *sql.Conn
	lockID int64
	held   bool
}

func NewSingleInstanceGuard(db *sql.DB) *SingleInstanceGuard {
	return &SingleInstanceGuard{db: db, lockID: LiveWorkerLockID}
}

func (g *SingleInstanceGuard) TryAcquire(ctx context.Context) error {
	conn, err := g.db.Conn(ctx)
	if err != nil {
		return fmt.Errorf("get dedicated connection: %w", err)
	}
	var acquired bool
	if err := conn.QueryRowContext(ctx, "SELECT pg_try_advisory_lock($1)", g.lockID).Scan(&acquired); err != nil {
		conn.Close()
		return fmt.Errorf("pg_try_advisory_lock: %w", err)
	}
	if !acquired {
		conn.Close()
		return fmt.Errorf("已有 TradeX 实盘 Worker 实例正在运行，拒绝启动第二个（LockId=%#x）", g.lockID)
	}
	g.conn = conn
	g.held = true
	return nil
}

func (g *SingleInstanceGuard) Release(ctx context.Context) error {
	if !g.held || g.conn == nil {
		return nil
	}
	g.held = false
	conn := g.conn
	g.conn = nil
	var released bool
	if err := conn.QueryRowContext(ctx, "SELECT pg_advisory_unlock($1)", g.lockID).Scan(&released); err != nil {
		conn.Close()
		return fmt.Errorf("pg_advisory_unlock: %w", err)
	}
	return conn.Close()
}
