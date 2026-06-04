using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionOpeningOrderId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OpeningOrderId",
                table: "Positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_OpeningOrderId",
                table: "Positions",
                column: "OpeningOrderId",
                unique: true,
                filter: "\"OpeningOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_StrategyId_Pair_Status",
                table: "Positions",
                columns: new[] { "StrategyId", "Pair", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Positions_OpeningOrderId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_StrategyId_Pair_Status",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "OpeningOrderId",
                table: "Positions");
        }
    }
}
