package schema

import (
	"time"

	"entgo.io/ent"
	"entgo.io/ent/dialect"
	"entgo.io/ent/schema/edge"
	"entgo.io/ent/schema/field"
	"entgo.io/ent/schema/index"
	"github.com/google/uuid"
)

type BacktestTask struct {
	ent.Schema
}

func (BacktestTask) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.UUID("strategy_id", uuid.UUID{}),
		field.String("exchange_id"),
		field.String("pair"),
		field.String("timeframe"),
		field.Float("initial_capital").
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("position_size").
			Optional().
			Nillable().
			SchemaType(map[string]string{dialect.Postgres: "numeric(20,8)"}),
		field.Float("fee_rate").
			Default(0).
			SchemaType(map[string]string{dialect.Postgres: "numeric(10,6)"}),
		field.Time("start_at"),
		field.Time("end_at"),
		field.Enum("status").
			Values("Pending", "Running", "Completed", "Failed", "Cancelled").
			Default("Pending"),
		field.Enum("phase").
			Values("Queued", "FetchingData", "Running").
			Optional(),
		field.Int("progress").Default(0),
		field.Text("error_message").Optional().Nillable(),
		field.Time("created_at").Default(time.Now).Immutable(),
		field.Time("updated_at").Default(time.Now).UpdateDefault(time.Now),
	}
}

func (BacktestTask) Edges() []ent.Edge {
	return []ent.Edge{
		edge.To("result", BacktestResult.Type).
			Unique().
			StorageKey(edge.Column("result_id")),

	}
}

func (BacktestTask) Indexes() []ent.Index {
	return []ent.Index{
		index.Fields("status"),
		index.Fields("strategy_id"),
		index.Fields("pair", "timeframe"),
	}
}
