package schema

import (
	"entgo.io/ent"
	"entgo.io/ent/dialect"
	"entgo.io/ent/schema/field"
	"github.com/google/uuid"
)

// Position 持仓。列严格对齐 C# EF 的 positions 表（共享同一库，schema 由 C# 维护）。
type Position struct {
	ent.Schema
}

func (Position) Fields() []ent.Field {
	num := map[string]string{dialect.Postgres: "numeric(28,12)"}
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.UUID("trader_id", uuid.UUID{}),
		field.UUID("exchange_id", uuid.UUID{}),
		field.UUID("strategy_id", uuid.UUID{}),
		field.UUID("opening_order_id", uuid.UUID{}).Optional().Nillable(),
		field.String("pair"),
		field.Float("quantity").SchemaType(num),
		field.Float("entry_price").SchemaType(num),
		field.Float("current_price").SchemaType(num),
		field.Float("unrealized_pnl").SchemaType(num),
		field.Float("realized_pnl").SchemaType(num),
		field.String("status"),
		field.Time("opened_at_utc"),
		field.Time("closed_at_utc").Optional().Nillable(),
		field.Time("updated_at"),
		field.UUID("version", uuid.UUID{}),
	}
}
