package schema

import (
	"entgo.io/ent"
	"entgo.io/ent/dialect"
	"entgo.io/ent/schema/field"
	"entgo.io/ent/schema/index"
	"github.com/google/uuid"
)

type BacktestKlineAnalysis struct {
	ent.Schema
}

func (BacktestKlineAnalysis) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.UUID("task_id", uuid.UUID{}),
		field.Int("kline_index"),
		field.Time("timestamp"),
		field.Float("open").
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("high").
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("low").
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("close").
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("volume").
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.JSON("indicator_values", map[string]float64{}).Optional(),
		field.Bool("entry_condition_result").Optional().Nillable(),
		field.Bool("exit_condition_result").Optional().Nillable(),
		field.Bool("in_position").Default(false),
		field.String("action").MaxLen(20).Default("none"),
		field.Float("avg_entry_price").
			Optional().Nillable().
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("position_quantity").
			Optional().Nillable().
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("position_cost").
			Optional().Nillable().
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("position_value").
			Optional().Nillable().
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("position_pnl").
			Optional().Nillable().
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("position_pnl_percent").
			Optional().Nillable().
			SchemaType(map[string]string{dialect.Postgres: "numeric(12,4)"}),
	}
}

func (BacktestKlineAnalysis) Indexes() []ent.Index {
	return []ent.Index{
		index.Fields("task_id", "kline_index").Unique(),
		index.Fields("task_id"),
	}
}
