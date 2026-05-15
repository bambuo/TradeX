using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Action = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Resource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResourceId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Detail = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpAddress = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BacktestKlineAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    High = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Low = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    IndicatorsJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntryConditionResult = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    ExitConditionResult = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    InPosition = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Action = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvgEntryPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    PositionQuantity = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    PositionCost = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    PositionValue = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    PositionPnl = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    PositionPnlPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestKlineAnalyses", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BacktestResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StrategyName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Pair = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    InitialCapital = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FinalValue = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TotalTrades = table.Column<int>(type: "int", nullable: false),
                    TotalReturnPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    AnnualizedReturnPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MaxDrawdownPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    WinRate = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    SharpeRatio = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ProfitLossRatio = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Details = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestResults", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BacktestTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StrategyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExchangeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StrategyName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Pair = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InitialCapital = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    PositionSize = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phase = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestTasks", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExchangePairRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExchangeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Pair = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PricePrecision = table.Column<int>(type: "int", nullable: false),
                    QuantityPrecision = table.Column<int>(type: "int", nullable: false),
                    MinNotional = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MinQuantity = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TickSize = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    StepSize = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangePairRules", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Exchanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TraderId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiKeyEncrypted = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SecretKeyEncrypted = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PassphraseEncrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TestResult = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exchanges", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MfaSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SecretKey = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsVerified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaSecrets", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NotificationChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfigEncrypted = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TraderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientOrderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExchangeOrderId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExchangeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StrategyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PositionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Pair = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Side = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FilledQuantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    QuoteQuantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FeeAsset = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsManual = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PlacedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Pairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExchangeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BaseAsset = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuoteAsset = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pairs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TraderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExchangeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StrategyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Pair = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Quantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    UnrealizedPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsUsed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Token = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Strategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntryCondition = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExitCondition = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExecutionRule = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Strategies", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StrategyBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StrategyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TraderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExchangeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Pairs = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyBindings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SystemConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Traders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvatarColor = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvatarUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Style = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Traders", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Username = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsMfaEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MfaSecretEncrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecoveryCodesJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestKlineAnalyses_TaskId",
                table: "BacktestKlineAnalyses",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestKlineAnalyses_TaskId_Index",
                table: "BacktestKlineAnalyses",
                columns: new[] { "TaskId", "Index" },
                unique: true);

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
                name: "IX_ExchangePairRules_ExchangeId",
                table: "ExchangePairRules",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_Exchanges_Name",
                table: "Exchanges",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaSecrets_UserId",
                table: "MfaSecrets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClientOrderId",
                table: "Orders",
                column: "ClientOrderId",
                unique: true);

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
                name: "IX_Pairs_ExchangeId_Name",
                table: "Pairs",
                columns: new[] { "ExchangeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_ExchangeId_Pair_Status",
                table: "Positions",
                columns: new[] { "ExchangeId", "Pair", "Status" });

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
                name: "IX_StrategyBindings_Status",
                table: "StrategyBindings",
                column: "Status");

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
                name: "BacktestKlineAnalyses");

            migrationBuilder.DropTable(
                name: "BacktestResults");

            migrationBuilder.DropTable(
                name: "BacktestTasks");

            migrationBuilder.DropTable(
                name: "ExchangePairRules");

            migrationBuilder.DropTable(
                name: "Exchanges");

            migrationBuilder.DropTable(
                name: "MfaSecrets");

            migrationBuilder.DropTable(
                name: "NotificationChannels");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Pairs");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "RecoveryCodes");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Strategies");

            migrationBuilder.DropTable(
                name: "StrategyBindings");

            migrationBuilder.DropTable(
                name: "SystemConfigs");

            migrationBuilder.DropTable(
                name: "Traders");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
