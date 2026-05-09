using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameStrategyDeploymentsTableToStrategyBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "StrategyDeployments",
                newName: "StrategyBindings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "StrategyBindings",
                newName: "StrategyDeployments");
        }
    }
}
