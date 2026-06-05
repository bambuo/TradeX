using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeOrderHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeOrderHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExchangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pair = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    FilledQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ExchangeOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlacedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeOrderHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrderHistories_ExchangeId_OrderId",
                table: "ExchangeOrderHistories",
                columns: new[] { "ExchangeId", "ExchangeOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrderHistories_ExchangeId_PlacedAt",
                table: "ExchangeOrderHistories",
                columns: new[] { "ExchangeId", "PlacedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeOrderHistories");
        }
    }
}
