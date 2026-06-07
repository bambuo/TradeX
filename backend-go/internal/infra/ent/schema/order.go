package schema

import (
	"entgo.io/ent"
	"entgo.io/ent/dialect"
	"entgo.io/ent/schema/field"
	"github.com/google/uuid"
)

// Order 订单。列严格对齐 C# EF 的 orders 表（共享同一库，schema 由 C# 维护）。
// 不声明索引/DB 默认值，避免 AutoMigrate 对共享表做变更。
type Order struct {
	ent.Schema
}

func (Order) Fields() []ent.Field {
	num := map[string]string{dialect.Postgres: "numeric(28,12)"}
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.UUID("trader_id", uuid.UUID{}),
		field.UUID("client_order_id", uuid.UUID{}),
		field.String("exchange_order_id").Optional().Nillable(),
		field.UUID("exchange_id", uuid.UUID{}),
		field.UUID("strategy_id", uuid.UUID{}).Optional().Nillable(),
		field.UUID("position_id", uuid.UUID{}).Optional().Nillable(),
		field.String("pair"),
		field.String("side"),
		field.String("type"),
		field.String("status"),
		field.Float("price").SchemaType(num).Optional().Nillable(),
		field.Float("quantity").SchemaType(num),
		field.Float("filled_quantity").SchemaType(num),
		field.Float("quote_quantity").SchemaType(num),
		field.Float("fee").SchemaType(num),
		field.String("fee_asset").Optional().Nillable(),
		field.Bool("is_manual"),
		field.Time("placed_at_utc"),
		field.Time("updated_at"),
		field.UUID("version", uuid.UUID{}),
	}
}
