package cli

import (
	"github.com/spf13/cobra"

	_ "github.com/tradex/backend-go/internal/config"
)

func NewRootCmd() *cobra.Command {
	cmd := &cobra.Command{
		Use:   "tradex",
		Short: "TradeX Backtest Engine",
	}

	cmd.AddCommand(NewAPICmd())
	cmd.AddCommand(NewWorkerCmd())

	return cmd
}

func Execute() error {
	return NewRootCmd().Execute()
}
