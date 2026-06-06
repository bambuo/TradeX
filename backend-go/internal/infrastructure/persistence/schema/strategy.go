package schema

import (
	"entgo.io/ent"
	"entgo.io/ent/schema/field"
	"github.com/google/uuid"
)

type Strategy struct {
	ent.Schema
}

func (Strategy) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.String("name"),
		field.Text("entry_condition").Optional(),
		field.Text("exit_condition").Optional(),
		field.Text("execution_rule").Optional(),
		field.String("exchange_id"),
		field.String("pair"),
		field.String("timeframe"),
		field.Bool("is_active").Default(true),
	}
}
