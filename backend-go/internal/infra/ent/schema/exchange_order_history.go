package schema

import (
	"time"

	"entgo.io/ent"
	"entgo.io/ent/dialect"
	"entgo.io/ent/schema/field"
	"github.com/google/uuid"
)

// ExchangeOrderHistory 定时从交易所拉取的历史订单，支撑本地分页查询。
//
// 列名严格对齐 C# EF 迁移建立的 exchange_order_histories 表（与 C# 后端共享同一库）。
// 该表的唯一约束 ix_exchange_order_histories_exchange_id_order_id 由 C# 维护，
// 供 upsert 的 ON CONFLICT (exchange_id, exchange_order_id) 使用，故此处不再声明索引，
// 避免 AutoMigrate 向共享库追加重复索引。
type ExchangeOrderHistory struct {
	ent.Schema
}

func (ExchangeOrderHistory) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.UUID("exchange_id", uuid.UUID{}),
		field.String("pair").Default(""),
		field.String("side").Default(""),
		field.String("type").Default(""),
		field.String("status").Default(""),
		field.Float("price").
			SchemaType(map[string]string{dialect.Postgres: "numeric(30,12)"}).Default(0),
		field.Float("quantity").
			SchemaType(map[string]string{dialect.Postgres: "numeric(30,12)"}).Default(0),
		field.Float("filled_quantity").
			SchemaType(map[string]string{dialect.Postgres: "numeric(30,12)"}).Default(0),
		field.String("exchange_order_id").Default(""),
		field.Time("placed_at"),
		field.Time("synced_at").Default(time.Now),
	}
}
