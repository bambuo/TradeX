package persistence

import (
	"context"

	"entgo.io/ent/dialect/sql"

	"tradex/internal/domain"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/exchangeorderhistory"
)

type exchangeOrderHistoryRepo struct {
	client *ent.Client
}

// NewExchangeOrderHistoryRepo 构造交易所历史订单仓储。
func NewExchangeOrderHistoryRepo(client *ent.Client) domain.ExchangeOrderHistoryRepository {
	return &exchangeOrderHistoryRepo{client: client}
}

// UpsertMany 按 (exchange_id, exchange_order_id) 唯一键插入或更新；冲突时刷新可变字段，保留原 id。
// 对应 C# IExchangeOrderHistoryRepository.UpsertManyAsync。
func (r *exchangeOrderHistoryRepo) UpsertMany(ctx context.Context, orders []*domain.ExchangeOrderHistory) error {
	if len(orders) == 0 {
		return nil
	}

	builders := make([]*ent.ExchangeOrderHistoryCreate, 0, len(orders))
	for _, o := range orders {
		builders = append(builders, r.client.ExchangeOrderHistory.Create().
			SetExchangeID(o.ExchangeID).
			SetPair(o.Pair).
			SetSide(string(o.Side)).
			SetType(string(o.Type)).
			SetStatus(string(o.Status)).
			SetPrice(f64(o.Price)).
			SetQuantity(f64(o.Quantity)).
			SetFilledQuantity(f64(o.FilledQuantity)).
			SetExchangeOrderID(o.ExchangeOrderID).
			SetPlacedAt(o.PlacedAt).
			SetSyncedAt(o.SyncedAt))
	}

	return r.client.ExchangeOrderHistory.CreateBulk(builders...).
		OnConflict(
			sql.ConflictColumns(
				exchangeorderhistory.FieldExchangeID,
				exchangeorderhistory.FieldExchangeOrderID,
			),
		).
		Update(func(u *ent.ExchangeOrderHistoryUpsert) {
			u.UpdatePair()
			u.UpdateSide()
			u.UpdateType()
			u.UpdateStatus()
			u.UpdatePrice()
			u.UpdateQuantity()
			u.UpdateFilledQuantity()
			u.UpdatePlacedAt()
			u.UpdateSyncedAt()
		}).
		Exec(ctx)
}
