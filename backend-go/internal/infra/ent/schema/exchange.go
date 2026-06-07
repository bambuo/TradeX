package schema

import (
	"time"

	"entgo.io/ent"
	"entgo.io/ent/schema/field"
	"github.com/google/uuid"
)

// Exchange 交易所配置（含加密后的密钥）。对应 C# TradeX.Core.Models.Exchange。
//
// 列名严格对齐 C# EF 迁移建立的 exchanges 表（共享同一库）。索引（name 唯一、status）
// 由 C# 维护，此处不声明，避免 AutoMigrate 追加重复索引。
type Exchange struct {
	ent.Schema
}

func (Exchange) Fields() []ent.Field {
	return []ent.Field{
		field.UUID("id", uuid.UUID{}).Default(uuid.New),
		field.String("name").Default(""),
		field.String("type").Default(""),
		field.Text("api_key_encrypted").Default(""),
		field.Text("secret_key_encrypted").Default(""),
		field.Text("passphrase_encrypted").Optional().Nillable(),
		field.String("status").Default("Enabled"),
		field.Time("last_tested_at").Optional().Nillable(),
		field.String("test_result").Optional().Nillable(),
		field.UUID("created_by", uuid.UUID{}).Default(uuid.New),
		field.Time("created_at").Default(time.Now).Immutable(),
		field.Time("updated_at").Default(time.Now).UpdateDefault(time.Now),
	}
}
