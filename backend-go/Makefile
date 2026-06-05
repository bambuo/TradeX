.PHONY: all build test lint clean ent-gen help

all: lint test build

# ─── Build ───────────────────────────────────────────────
build:
	go build -o bin/tradex ./cmd/tradex

build-api:
	go build -o bin/tradex ./cmd/tradex

build-worker:
	go build -o bin/tradex ./cmd/tradex

# ─── Run ─────────────────────────────────────────────────
run-api:
	go run ./cmd/tradex api

run-worker:
	go run ./cmd/tradex worker

# ─── Test ─────────────────────────────────────────────────
test:
	go test ./... -v -count=1 -race

test-short:
	go test ./... -count=1 -short

test-parity:
	go test ./internal/engine/ -run Parity -v -count=1

test-coverage:
	go test ./... -count=1 -race -coverprofile=coverage.out
	go tool cover -html=coverage.out -o coverage.html

# ─── Lint ────────────────────────────────────────────────
lint:
	golangci-lint run ./...

# ─── Database ────────────────────────────────────────────
ent-gen:
	go generate ./internal/infra/persistence/...

# ─── Clean ───────────────────────────────────────────────
clean:
	rm -rf bin/ coverage.out coverage.html

# ─── Go tools ────────────────────────────────────────────
tidy:
	go mod tidy

vet:
	go vet ./...
