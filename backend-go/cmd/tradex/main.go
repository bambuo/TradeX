package main

import (
	"os"

	"github.com/rs/zerolog"

	"tradex/internal/interfaces/cli"
)

func main() {
	if err := cli.Execute(); err != nil {
		l := zerolog.New(os.Stderr)
		l.Fatal().Err(err).Msg("cli failed")
	}
}
