package guard

import (
	"context"
	"database/sql"
	"os"
	"testing"

	_ "github.com/jackc/pgx/v5/stdlib"
)

func openTestDB(t *testing.T) *sql.DB {
	t.Helper()
	dsn := os.Getenv("DATABASE_DSN")
	if dsn == "" {
		t.Skip("未设置 DATABASE_DSN，跳过 advisory 锁集成测试")
	}
	db, err := sql.Open("pgx", dsn)
	if err != nil {
		t.Fatalf("open db: %v", err)
	}
	if err := db.PingContext(context.Background()); err != nil {
		t.Skipf("无法连接数据库（%v），跳过", err)
	}
	return db
}

func TestSingleInstanceGuard_SecondAcquireRejected(t *testing.T) {
	ctx := context.Background()
	db1 := openTestDB(t)
	defer db1.Close()
	db2 := openTestDB(t)
	defer db2.Close()

	g1 := NewSingleInstanceGuard(db1)
	if err := g1.TryAcquire(ctx); err != nil {
		t.Fatalf("第一个实例应成功获取锁: %v", err)
	}

	g2 := NewSingleInstanceGuard(db2)
	if err := g2.TryAcquire(ctx); err == nil {
		_ = g2.Release(ctx)
		t.Fatal("第二个实例应被拒绝，但获取成功")
	}

	if err := g1.Release(ctx); err != nil {
		t.Fatalf("释放锁失败: %v", err)
	}
	if err := g2.TryAcquire(ctx); err != nil {
		t.Fatalf("锁释放后第二个实例应能获取: %v", err)
	}
	if err := g2.Release(ctx); err != nil {
		t.Fatalf("释放锁失败: %v", err)
	}
}

func TestSingleInstanceGuard_ReleaseIdempotent(t *testing.T) {
	ctx := context.Background()
	db := openTestDB(t)
	defer db.Close()

	g := NewSingleInstanceGuard(db)
	if err := g.TryAcquire(ctx); err != nil {
		t.Fatalf("acquire: %v", err)
	}
	if err := g.Release(ctx); err != nil {
		t.Fatalf("first release: %v", err)
	}
	if err := g.Release(ctx); err != nil {
		t.Fatalf("重复 Release 应为 no-op: %v", err)
	}
}
