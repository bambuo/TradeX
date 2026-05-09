using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameBacktestCandleAnalysesToKlineAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop old table, create new table with Kline name
            migrationBuilder.DropTable(
                name: "BacktestCandleAnalyses");

            migrationBuilder.CreateTable(
                name: "BacktestKlineAnalyses",
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
                    table.PrimaryKey("PK_BacktestKlineAnalyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BacktestKlineAnalyses_TaskId",
                table: "BacktestKlineAnalyses",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestKlineAnalyses_TaskId_Index",
                table: "BacktestKlineAnalyses",
                columns: new[] { "TaskId", "Index" },
                unique: true);

            // Strategy: rename columns (drop Json suffix)
            migrationBuilder.RenameColumn(
                name: "EntryConditionJson",
                table: "Strategies",
                newName: "EntryCondition");

            migrationBuilder.RenameColumn(
                name: "ExitConditionJson",
                table: "Strategies",
                newName: "ExitCondition");

            migrationBuilder.RenameColumn(
                name: "ExecutionRuleJson",
                table: "Strategies",
                newName: "ExecutionRule");

            // StrategyBinding: rename column
            migrationBuilder.RenameColumn(
                name: "Pairs",
                table: "StrategyBindings",
                newName: "Pairs");

            // BacktestTask: rename columns
            migrationBuilder.RenameColumn(
                name: "Pair",
                table: "BacktestTasks",
                newName: "Pair");

            migrationBuilder.RenameColumn(
                name: "StartAtUtc",
                table: "BacktestTasks",
                newName: "StartAt");

            migrationBuilder.RenameColumn(
                name: "EndAtUtc",
                table: "BacktestTasks",
                newName: "EndAt");

            // Add new columns for BacktestTask
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "BacktestTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PositionSize",
                table: "BacktestTasks",
                type: "TEXT",
                nullable: true);

            // Drop old column, add new ones for BacktestResult
            migrationBuilder.DropColumn(
                name: "DetailJson",
                table: "BacktestResults");

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StrategyName",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Pair",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Timeframe",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "StartAt",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "EndAt",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "InitialCapital",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalValue",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            // Drop old column, add new one for BacktestTask
            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "BacktestTasks");

            // Remove deployment id from backtest tasks
            migrationBuilder.DropColumn(
                name: "DeploymentId",
                table: "BacktestTasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestKlineAnalyses");

            // Revert Strategy
            migrationBuilder.RenameColumn(
                name: "EntryCondition",
                table: "Strategies",
                newName: "EntryConditionJson");

            migrationBuilder.RenameColumn(
                name: "ExitCondition",
                table: "Strategies",
                newName: "ExitConditionJson");

            migrationBuilder.RenameColumn(
                name: "ExecutionRule",
                table: "Strategies",
                newName: "ExecutionRuleJson");

            // Revert StrategyBinding
            migrationBuilder.RenameColumn(
                name: "Pairs",
                table: "StrategyBindings",
                newName: "Pairs");

            // Revert BacktestTask
            migrationBuilder.RenameColumn(
                name: "Pair",
                table: "BacktestTasks",
                newName: "Pair");

            migrationBuilder.RenameColumn(
                name: "StartAt",
                table: "BacktestTasks",
                newName: "StartAtUtc");

            migrationBuilder.RenameColumn(
                name: "EndAt",
                table: "BacktestTasks",
                newName: "EndAtUtc");

            // Remove BacktestResult new columns
            migrationBuilder.DropColumn(
                name: "StrategyName",
                table: "BacktestResults");

            migrationBuilder.DropColumn(
                name: "Pair",
                table: "BacktestResults");

            migrationBuilder.DropColumn(
                name: "Timeframe",
                table: "BacktestResults");

            migrationBuilder.DropColumn(
                name: "StartAt",
                table: "BacktestResults");

            migrationBuilder.DropColumn(
                name: "EndAt",
                table: "BacktestResults");

            migrationBuilder.DropColumn(
                name: "InitialCapital",
                table: "BacktestResults");

            migrationBuilder.DropColumn(
                name: "FinalValue",
                table: "BacktestResults");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "BacktestResults");

            // Restore DetailJson
            migrationBuilder.AddColumn<string>(
                name: "DetailJson",
                table: "BacktestResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Restore BacktestTask columns
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "BacktestTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeploymentId",
                table: "BacktestTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Re-add old table
            migrationBuilder.CreateTable(
                name: "BacktestCandleAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AvgEntryPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    Close = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryConditionResult = table.Column<bool>(type: "INTEGER", nullable: true),
                    ExitConditionResult = table.Column<bool>(type: "INTEGER", nullable: true),
                    High = table.Column<decimal>(type: "TEXT", nullable: false),
                    InPosition = table.Column<bool>(type: "INTEGER", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    IndicatorsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Low = table.Column<decimal>(type: "TEXT", nullable: false),
                    Open = table.Column<decimal>(type: "TEXT", nullable: false),
                    PositionCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionPnl = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionPnlPercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionQuantity = table.Column<decimal>(type: "TEXT", nullable: true),
                    PositionValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Volume = table.Column<decimal>(type: "TEXT", nullable: false)
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

            // Remove BacktestTask new columns
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "BacktestTasks");

            migrationBuilder.DropColumn(
                name: "PositionSize",
                table: "BacktestTasks");
        }
    }
}
