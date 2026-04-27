using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropStrategyDeploymentJsonColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntryConditionJson",
                table: "StrategyDeployments");

            migrationBuilder.DropColumn(
                name: "ExecutionRuleJson",
                table: "StrategyDeployments");

            migrationBuilder.DropColumn(
                name: "ExitConditionJson",
                table: "StrategyDeployments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntryConditionJson",
                table: "StrategyDeployments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExecutionRuleJson",
                table: "StrategyDeployments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExitConditionJson",
                table: "StrategyDeployments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
