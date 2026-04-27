using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateBacktestCandleAnalysesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnalysisJson",
                table: "BacktestResults");

            migrationBuilder.CreateTable(
                name: "BacktestCandleAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Open = table.Column<decimal>(type: "TEXT", nullable: false),
                    High = table.Column<decimal>(type: "TEXT", nullable: false),
                    Low = table.Column<decimal>(type: "TEXT", nullable: false),
                    Close = table.Column<decimal>(type: "TEXT", nullable: false),
                    Volume = table.Column<decimal>(type: "TEXT", nullable: false),
                    IndicatorsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    EntryConditionResult = table.Column<bool>(type: "INTEGER", nullable: true),
                    ExitConditionResult = table.Column<bool>(type: "INTEGER", nullable: true),
                    InPosition = table.Column<bool>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AvgEntryPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionQuantity = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionPnl = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionPnlPercent = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestCandleAnalyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BacktestCandleAnalyses_TaskId",
                table: "BacktestCandleAnalyses",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestCandleAnalyses_TaskId_Index",
                table: "BacktestCandleAnalyses",
                columns: new[] { "TaskId", "Index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestCandleAnalyses");

            migrationBuilder.AddColumn<string>(
                name: "AnalysisJson",
                table: "BacktestResults",
                type: "TEXT",
                nullable: true);
        }
    }
}
