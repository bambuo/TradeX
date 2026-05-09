using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameSymbolIdToPairInPositionsAndOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Positions\" RENAME COLUMN \"SymbolId\" TO \"Pair\";");
            migrationBuilder.Sql("ALTER TABLE \"Orders\" RENAME COLUMN \"SymbolId\" TO \"Pair\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Positions\" RENAME COLUMN \"Pair\" TO \"SymbolId\";");
            migrationBuilder.Sql("ALTER TABLE \"Orders\" RENAME COLUMN \"Pair\" TO \"SymbolId\";");
        }
    }
}
