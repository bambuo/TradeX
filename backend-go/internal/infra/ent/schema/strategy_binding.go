package schema

import (
	"entgo.io/ent"
	"entgo.io/ent/dialect"
	"entgo.io/ent/schema/field"
	"github.com/google/uuid"
)

// StrategyBinding 策略绑定。列严格对齐 C# EF 的 strategy_bindings 表（共享同一库）。
type StrategyBinding struct {
	ent.Schema
}

func (StrategyBinding) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.UUID("strategy_id", uuid.UUID{}),
		field.String("name").SchemaType(map[string]string{dialect.Postgres: "text"}),
		field.UUID("trader_id", uuid.UUID{}),
		field.UUID("exchange_id", uuid.UUID{}),
		field.String("pairs"),
		field.String("timeframe"),
		field.String("status"),
		field.UUID("created_by", uuid.UUID{}),
		field.Time("created_at"),
		field.Time("updated_at"),
	}
}
