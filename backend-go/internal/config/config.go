package config

import (
	"fmt"
	"os"

	"github.com/joho/godotenv"
)

func init() {
	_ = godotenv.Load()
}

func init() {
	if os.Getenv("DATABASE_DSN") == "" && os.Getenv("TX_DB_HOST") != "" {
		dsn := fmt.Sprintf("postgres://%s:%s@%s:%s/%s?sslmode=disable",
			os.Getenv("TX_DB_USER"),
			os.Getenv("TX_DB_PASSWORD"),
			os.Getenv("TX_DB_HOST"),
			os.Getenv("TX_DB_PORT"),
			os.Getenv("TX_DB_NAME"),
		)
		os.Setenv("DATABASE_DSN", dsn)
	}

	if os.Getenv("REDIS_ADDR") == "" && os.Getenv("TX_REDIS_HOST") != "" {
		addr := fmt.Sprintf("%s:%s", os.Getenv("TX_REDIS_HOST"), os.Getenv("TX_REDIS_PORT"))
		os.Setenv("REDIS_ADDR", addr)
	}

	if os.Getenv("ENCRYPTION_KEY") == "" && os.Getenv("TX_ENCRYPTION_KEY") != "" {
		os.Setenv("ENCRYPTION_KEY", os.Getenv("TX_ENCRYPTION_KEY"))
	}

	setDefault("LISTEN", ":8080")
	setDefault("ENVIRONMENT", "development")
}

func setDefault(key, val string) {
	if os.Getenv(key) == "" {
		os.Setenv(key, val)
	}
}
