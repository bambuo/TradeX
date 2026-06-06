package storage

import (
	"context"
	"database/sql"
	"fmt"

	_ "github.com/jackc/pgx/v5/stdlib"

	"entgo.io/ent/dialect"
	entsql "entgo.io/ent/dialect/sql"

	"tradex/internal/infrastructure/persistence/ent"
	"tradex/internal/infrastructure/persistence/ent/migrate"
)

func AutoMigrate(ctx context.Context, client *ent.Client) error {
	return client.Schema.Create(ctx,
		migrate.WithDropIndex(false),
		migrate.WithDropColumn(false),
	)
}

func OpenDB(dsn string) (*ent.Client, error) {
	db, err := sql.Open("pgx", dsn)
	if err != nil {
		return nil, fmt.Errorf("open database: %w", err)
	}
	drv := entsql.OpenDB(dialect.Postgres, db)
	return ent.NewClient(ent.Driver(drv)), nil
}
