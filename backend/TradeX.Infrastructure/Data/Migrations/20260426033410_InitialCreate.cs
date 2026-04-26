using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Resource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BacktestResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalReturnPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    AnnualizedReturnPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxDrawdownPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    WinRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    SharpeRatio = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProfitLossRatio = table.Column<decimal>(type: "TEXT", nullable: false),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: false),
                    AnalysisJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BacktestTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeploymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrategyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrategyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SymbolId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Timeframe = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    InitialCapital = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    StartAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TraderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApiKeyEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                    SecretKeyEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                    PassphraseEncrypted = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastTestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TestResult = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeSymbolRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PricePrecision = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityPrecision = table.Column<int>(type: "INTEGER", nullable: false),
                    MinNotional = table.Column<decimal>(type: "TEXT", nullable: false),
                    MinQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    TickSize = table.Column<decimal>(type: "TEXT", nullable: false),
                    StepSize = table.Column<decimal>(type: "TEXT", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeSymbolRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MfaSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecretKey = table.Column<string>(type: "TEXT", nullable: false),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaSecrets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ConfigEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastTestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TraderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeOrderId = table.Column<string>(type: "TEXT", nullable: true),
                    ExchangeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrategyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PositionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SymbolId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Side = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    FilledQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    QuoteQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Fee = table.Column<decimal>(type: "TEXT", nullable: false),
                    FeeAsset = table.Column<string>(type: "TEXT", nullable: true),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlacedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TraderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrategyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SymbolId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnrealizedPnl = table.Column<decimal>(type: "TEXT", nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Strategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntryConditionJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExitConditionJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutionRuleJson = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Strategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyDeployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrategyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TraderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SymbolIds = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Timeframe = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    EntryConditionJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExitConditionJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutionRuleJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyDeployments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Symbols",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SymbolName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BaseAsset = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QuoteAsset = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Symbols", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Traders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Traders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsMfaEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MfaSecretEncrypted = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    RecoveryCodesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestResults_TaskId",
                table: "BacktestResults",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BacktestTasks_ExchangeId",
                table: "BacktestTasks",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestTasks_Status",
                table: "BacktestTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestTasks_StrategyId",
                table: "BacktestTasks",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeAccounts_Name",
                table: "ExchangeAccounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeSymbolRules_ExchangeId",
                table: "ExchangeSymbolRules",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaSecrets_UserId",
                table: "MfaSecrets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ExchangeOrderId",
                table: "Orders",
                column: "ExchangeOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TraderId",
                table: "Orders",
                column: "TraderId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_ExchangeId_SymbolId_Status",
                table: "Positions",
                columns: new[] { "ExchangeId", "SymbolId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_TraderId_Status",
                table: "Positions",
                columns: new[] { "TraderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId_Code",
                table: "RecoveryCodes",
                columns: new[] { "UserId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Strategies_Name",
                table: "Strategies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyDeployments_Status",
                table: "StrategyDeployments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyDeployments_TraderId_StrategyId",
                table: "StrategyDeployments",
                columns: new[] { "TraderId", "StrategyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_ExchangeId_SymbolName",
                table: "Symbols",
                columns: new[] { "ExchangeId", "SymbolName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemConfigs_Key",
                table: "SystemConfigs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Traders_UserId_Name",
                table: "Traders",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BacktestResults");

            migrationBuilder.DropTable(
                name: "BacktestTasks");

            migrationBuilder.DropTable(
                name: "ExchangeAccounts");

            migrationBuilder.DropTable(
                name: "ExchangeSymbolRules");

            migrationBuilder.DropTable(
                name: "MfaSecrets");

            migrationBuilder.DropTable(
                name: "NotificationChannels");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "RecoveryCodes");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Strategies");

            migrationBuilder.DropTable(
                name: "StrategyDeployments");

            migrationBuilder.DropTable(
                name: "Symbols");

            migrationBuilder.DropTable(
                name: "SystemConfigs");

            migrationBuilder.DropTable(
                name: "Traders");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
