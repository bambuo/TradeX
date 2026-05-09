using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameSymbolToPair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Symbols",
                newName: "Pairs");

            migrationBuilder.RenameColumn(
                name: "SymbolName",
                table: "Pairs",
                newName: "Name");

            migrationBuilder.RenameIndex(
                name: "IX_Symbols_ExchangeId_SymbolName",
                table: "Pairs",
                newName: "IX_Pairs_ExchangeId_Name");

            migrationBuilder.RenameTable(
                name: "ExchangeSymbolRules",
                newName: "ExchangePairRules");

            migrationBuilder.RenameColumn(
                name: "Symbol",
                table: "ExchangePairRules",
                newName: "Pair");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Pair",
                table: "ExchangePairRules",
                newName: "Symbol");

            migrationBuilder.RenameTable(
                name: "ExchangePairRules",
                newName: "ExchangeSymbolRules");

            migrationBuilder.RenameIndex(
                name: "IX_Pairs_ExchangeId_Name",
                table: "Pairs",
                newName: "IX_Symbols_ExchangeId_SymbolName");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Pairs",
                newName: "SymbolName");

            migrationBuilder.RenameTable(
                name: "Pairs",
                newName: "Symbols");
        }
    }
}
