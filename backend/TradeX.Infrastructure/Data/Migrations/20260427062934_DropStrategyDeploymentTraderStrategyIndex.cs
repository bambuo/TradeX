using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropStrategyDeploymentTraderStrategyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StrategyDeployments_TraderId_StrategyId",
                table: "StrategyDeployments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StrategyDeployments_TraderId_StrategyId",
                table: "StrategyDeployments",
                columns: new[] { "TraderId", "StrategyId" },
                unique: true);
        }
    }
}
