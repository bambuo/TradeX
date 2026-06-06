package schema

import (
	"time"

	"entgo.io/ent"
	"entgo.io/ent/schema/field"
	"entgo.io/ent/schema/index"
	"github.com/google/uuid"
)

type Strategy struct {
	ent.Schema
}

func (Strategy) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.String("name").MaxLen(100),
		field.Text("entry_condition").Optional(),
		field.Text("exit_condition").Optional(),
		field.Text("execution_rule").Optional(),
		field.Int("version").Default(1),
		field.UUID("created_by", uuid.UUID{}).Default(uuid.New),
		field.Time("created_at").Default(time.Now).Immutable(),
		field.Time("updated_at").Default(time.Now).UpdateDefault(time.Now),
	}
}

func (Strategy) Indexes() []ent.Index {
	return []ent.Index{
		index.Fields("name").Unique(),
	}
}
