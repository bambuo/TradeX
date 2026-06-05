package schema

import (
	"time"

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
		field.JSON("entry_condition_result", map[string]any{}).Optional(),
		field.JSON("exit_condition_result", map[string]any{}).Optional(),
		field.Bool("in_position").Default(false),
		field.String("action").Default("hold"),
		field.Float("position_value").
			Default(0).
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("position_pnl").
			Default(0).
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Time("created_at").Default(time.Now).Immutable(),
	}
}

func (BacktestKlineAnalysis) Indexes() []ent.Index {
	return []ent.Index{
		index.Fields("task_id", "kline_index").Unique(),
		index.Fields("task_id"),
	}
}
