package schema

import (
	"time"

	"entgo.io/ent"
	"entgo.io/ent/dialect"
	"entgo.io/ent/schema/edge"
	"entgo.io/ent/schema/field"
	"github.com/google/uuid"
)

type BacktestResult struct {
	ent.Schema
}

func (BacktestResult) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.String("strategy_name").Default(""),
		field.Float("final_value").
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("total_return_percent").
			SchemaType(map[string]string{dialect.Postgres: "numeric(12,4)"}),
		field.Float("annualized_return_percent").
			SchemaType(map[string]string{dialect.Postgres: "numeric(12,4)"}),
		field.Float("max_drawdown_percent").
			SchemaType(map[string]string{dialect.Postgres: "numeric(10,4)"}),
		field.Float("win_rate").
			SchemaType(map[string]string{dialect.Postgres: "numeric(8,4)"}),
		field.Float("sharpe_ratio").
			SchemaType(map[string]string{dialect.Postgres: "numeric(10,4)"}),
		field.Float("profit_loss_ratio").
			SchemaType(map[string]string{dialect.Postgres: "numeric(12,4)"}),
		field.Int("total_trades"),
		field.JSON("details", []byte{}).
			Optional(),
		field.Time("created_at").Default(time.Now).Immutable(),
	}
}

func (BacktestResult) Edges() []ent.Edge {
	return []ent.Edge{
		edge.From("task", BacktestTask.Type).
			Ref("result").
			Unique().
			Required(),
	}
}
