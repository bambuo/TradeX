package persistence

import (
	"context"
	"os"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/exchangeorderhistory"
)

func openIntegrationDB(t *testing.T) *ent.Client {
	t.Helper()
	dsn := os.Getenv("DATABASE_DSN")
	if dsn == "" {
		t.Skip("未设置 DATABASE_DSN，跳过持久化集成测试")
	}
	client, err := OpenDB(dsn)
	if err != nil {
		t.Skipf("打开数据库失败（%v），跳过", err)
	}
	if err := AutoMigrate(context.Background(), client); err != nil {
		t.Fatalf("迁移失败: %v", err)
	}
	return client
}

func TestExchangeRepo_And_HistoryUpsert(t *testing.T) {
	ctx := context.Background()
	client := openIntegrationDB(t)
	defer client.Close()

	exRepo := NewExchangeRepo(client)
	histRepo := NewExchangeOrderHistoryRepo(client)

	exID := uuid.New()
	name := "it-" + exID.String()[:8]
	_, err := client.Exchange.Create().
		SetID(exID).SetName(name).SetType(string(domain.ExchangeTypeBinance)).
		SetAPIKeyEncrypted("enc").SetSecretKeyEncrypted("enc").
		SetStatus(string(domain.ExchangeStatusEnabled)).
		Save(ctx)
	if err != nil {
		t.Fatalf("seed exchange: %v", err)
	}
	t.Cleanup(func() {
		_, _ = client.ExchangeOrderHistory.Delete().Where(exchangeorderhistory.ExchangeID(exID)).Exec(ctx)
		_ = client.Exchange.DeleteOneID(exID).Exec(ctx)
	})

	// GetAllEnabled 应包含新建交易所
	enabled, err := exRepo.GetAllEnabled(ctx)
	if err != nil {
		t.Fatalf("GetAllEnabled: %v", err)
	}
	found := false
	for _, e := range enabled {
		if e.ID == exID {
			found = true
			if e.Type != domain.ExchangeTypeBinance || !e.IsEnabled() {
				t.Fatalf("交易所映射错误: %+v", e)
			}
		}
	}
	if !found {
		t.Fatal("GetAllEnabled 未返回新建交易所")
	}

	now := time.Now().UTC().Truncate(time.Millisecond)
	mk := func(oid, status string, price string) *domain.ExchangeOrderHistory {
		return &domain.ExchangeOrderHistory{
			ExchangeID: exID, Pair: "BTCUSDT", Side: "Buy", Type: "Market", Status: domain.OrderStatus(status),
			Price: decimal.RequireFromString(price), Quantity: decimal.RequireFromString("1"),
			FilledQuantity: decimal.RequireFromString("1"), ExchangeOrderID: oid, PlacedAt: now, SyncedAt: now,
		}
	}

	// 首次 upsert 两条
	if err := histRepo.UpsertMany(ctx, []*domain.ExchangeOrderHistory{mk("1", "New", "100"), mk("2", "Filled", "200")}); err != nil {
		t.Fatalf("首次 upsert: %v", err)
	}
	count, _ := client.ExchangeOrderHistory.Query().Where(exchangeorderhistory.ExchangeID(exID)).Count(ctx)
	if count != 2 {
		t.Fatalf("首次后应为 2 条，实际 %d", count)
	}

	// 再次 upsert：order 1 状态变更 + 新增 order 3 → 共 3 条，order1 状态更新
	if err := histRepo.UpsertMany(ctx, []*domain.ExchangeOrderHistory{mk("1", "Filled", "111"), mk("3", "New", "300")}); err != nil {
		t.Fatalf("二次 upsert: %v", err)
	}
	count, _ = client.ExchangeOrderHistory.Query().Where(exchangeorderhistory.ExchangeID(exID)).Count(ctx)
	if count != 3 {
		t.Fatalf("二次后应为 3 条（去重 order1），实际 %d", count)
	}
	row, err := client.ExchangeOrderHistory.Query().
		Where(exchangeorderhistory.ExchangeID(exID), exchangeorderhistory.ExchangeOrderID("1")).Only(ctx)
	if err != nil {
		t.Fatalf("查 order1: %v", err)
	}
	if row.Status != "Filled" {
		t.Fatalf("order1 状态应更新为 Filled，实际 %q", row.Status)
	}
}
