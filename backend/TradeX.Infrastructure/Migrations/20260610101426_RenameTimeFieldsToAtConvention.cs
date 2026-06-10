using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameTimeFieldsToAtConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "opened_at_utc",
                table: "positions",
                newName: "opened_at");

            migrationBuilder.RenameColumn(
                name: "closed_at_utc",
                table: "positions",
                newName: "closed_at");

            migrationBuilder.RenameColumn(
                name: "placed_at_utc",
                table: "orders",
                newName: "placed_at");

            migrationBuilder.RenameColumn(
                name: "fetched_at_utc",
                table: "exchange_pair_rules",
                newName: "fetched_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "opened_at",
                table: "positions",
                newName: "opened_at_utc");

            migrationBuilder.RenameColumn(
                name: "closed_at",
                table: "positions",
                newName: "closed_at_utc");

            migrationBuilder.RenameColumn(
                name: "placed_at",
                table: "orders",
                newName: "placed_at_utc");

            migrationBuilder.RenameColumn(
                name: "fetched_at",
                table: "exchange_pair_rules",
                newName: "fetched_at_utc");
        }
    }
}
