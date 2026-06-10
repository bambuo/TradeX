using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropStrategyEntryExitConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entry_condition",
                table: "strategies");

            migrationBuilder.DropColumn(
                name: "exit_condition",
                table: "strategies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "entry_condition",
                table: "strategies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "exit_condition",
                table: "strategies",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
