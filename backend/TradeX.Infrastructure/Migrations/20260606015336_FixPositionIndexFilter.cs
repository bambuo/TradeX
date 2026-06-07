using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPositionIndexFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_positions_opening_order_id",
                table: "positions");

            migrationBuilder.CreateIndex(
                name: "ix_positions_opening_order_id",
                table: "positions",
                column: "opening_order_id",
                unique: true,
                filter: "opening_order_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_positions_opening_order_id",
                table: "positions");

            migrationBuilder.CreateIndex(
                name: "ix_positions_opening_order_id",
                table: "positions",
                column: "opening_order_id",
                unique: true,
                filter: "\"opening_order_id\" IS NOT NULL");
        }
    }
}
