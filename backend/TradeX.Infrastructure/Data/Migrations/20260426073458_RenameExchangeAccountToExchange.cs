using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameExchangeAccountToExchange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "ExchangeAccounts",
                newName: "Exchanges");

            migrationBuilder.RenameIndex(
                name: "IX_ExchangeAccounts_Name",
                table: "Exchanges",
                newName: "IX_Exchanges_Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Exchanges",
                newName: "ExchangeAccounts");

            migrationBuilder.RenameIndex(
                name: "IX_Exchanges_Name",
                table: "ExchangeAccounts",
                newName: "IX_ExchangeAccounts_Name");
        }
    }
}
