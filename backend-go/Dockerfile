# syntax=docker/dockerfile:1

# ─── Build stage ──────────────────────────────────────────
FROM golang:1.26-alpine AS build

RUN apk add --no-cache gcc musl-dev

WORKDIR /app

COPY go.mod go.sum ./
RUN go mod download

COPY . .

RUN CGO_ENABLED=0 go build -o /build/api ./cmd/tradex
RUN CGO_ENABLED=0 go build -o /build/worker ./cmd/tradex

# ─── API runtime ──────────────────────────────────────────
FROM alpine:3.21 AS api

RUN addgroup -S app && adduser -S -G app app

COPY --from=build /build/api /app/tradex

USER app
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s \
  CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["/app/tradex", "api"]

# ─── Worker runtime ───────────────────────────────────────
FROM alpine:3.21 AS worker

RUN addgroup -S app && adduser -S -G app app

COPY --from=build /build/worker /app/tradex

USER app

ENTRYPOINT ["/app/tradex", "worker"]
