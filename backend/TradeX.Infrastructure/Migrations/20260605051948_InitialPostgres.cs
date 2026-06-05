using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<string>(type: "text", nullable: true),
                    detail = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backtest_kline_analyses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<decimal>(type: "numeric", nullable: false),
                    high = table.Column<decimal>(type: "numeric", nullable: false),
                    low = table.Column<decimal>(type: "numeric", nullable: false),
                    close = table.Column<decimal>(type: "numeric", nullable: false),
                    volume = table.Column<decimal>(type: "numeric", nullable: false),
                    indicators_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    entry_condition_result = table.Column<bool>(type: "boolean", nullable: true),
                    exit_condition_result = table.Column<bool>(type: "boolean", nullable: true),
                    in_position = table.Column<bool>(type: "boolean", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    avg_entry_price = table.Column<decimal>(type: "numeric", nullable: true),
                    position_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    position_cost = table.Column<decimal>(type: "numeric", nullable: true),
                    position_value = table.Column<decimal>(type: "numeric", nullable: true),
                    position_pnl = table.Column<decimal>(type: "numeric", nullable: true),
                    position_pnl_percent = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_kline_analyses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backtest_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_name = table.Column<string>(type: "text", nullable: false),
                    pair = table.Column<string>(type: "text", nullable: false),
                    timeframe = table.Column<string>(type: "text", nullable: false),
                    start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    initial_capital = table.Column<decimal>(type: "numeric", nullable: false),
                    final_value = table.Column<decimal>(type: "numeric", nullable: false),
                    total_trades = table.Column<int>(type: "integer", nullable: false),
                    total_return_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    annualized_return_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    max_drawdown_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    win_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    sharpe_ratio = table.Column<decimal>(type: "numeric", nullable: false),
                    profit_loss_ratio = table.Column<decimal>(type: "numeric", nullable: false),
                    details = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backtest_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pair = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    initial_capital = table.Column<decimal>(type: "numeric", nullable: false),
                    position_size = table.Column<decimal>(type: "numeric", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_order_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pair = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    filled_quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    exchange_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    placed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_order_histories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_pair_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pair = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    price_precision = table.Column<int>(type: "integer", nullable: false),
                    quantity_precision = table.Column<int>(type: "integer", nullable: false),
                    min_notional = table.Column<decimal>(type: "numeric", nullable: false),
                    min_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    tick_size = table.Column<decimal>(type: "numeric", nullable: false),
                    step_size = table.Column<decimal>(type: "numeric", nullable: false),
                    fetched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_pair_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchanges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    api_key_encrypted = table.Column<string>(type: "text", nullable: false),
                    secret_key_encrypted = table.Column<string>(type: "text", nullable: false),
                    passphrase_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_tested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    test_result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchanges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kline_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pair = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<decimal>(type: "numeric", nullable: false),
                    high = table.Column<decimal>(type: "numeric", nullable: false),
                    low = table.Column<decimal>(type: "numeric", nullable: false),
                    close = table.Column<decimal>(type: "numeric", nullable: false),
                    volume = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kline_cache", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mfa_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_key = table.Column<string>(type: "text", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_secrets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    config_encrypted = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_tested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pair = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    filled_quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    quote_quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    fee_asset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    placed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pairs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    base_asset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quote_asset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pairs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pair = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    current_price = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    unrealized_pnl = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    realized_pnl = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_used = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "strategies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entry_condition = table.Column<string>(type: "text", nullable: false),
                    exit_condition = table.Column<string>(type: "text", nullable: false),
                    execution_rule = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "strategy_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    trader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pairs = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "traders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    avatar_color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    style = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_traders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_secret_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    recovery_codes_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_kline_analyses_task_id",
                table: "backtest_kline_analyses",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_kline_analyses_task_id_index",
                table: "backtest_kline_analyses",
                columns: new[] { "task_id", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_backtest_results_task_id",
                table: "backtest_results",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_backtest_tasks_exchange_id",
                table: "backtest_tasks",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_tasks_status",
                table: "backtest_tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_tasks_strategy_id",
                table: "backtest_tasks",
                column: "strategy_id");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_order_histories_exchange_id_order_id",
                table: "exchange_order_histories",
                columns: new[] { "exchange_id", "exchange_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_order_histories_exchange_id_placed_at",
                table: "exchange_order_histories",
                columns: new[] { "exchange_id", "placed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_pair_rules_exchange_id",
                table: "exchange_pair_rules",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "ix_exchanges_name",
                table: "exchanges",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kline_cache_exchange_id_pair_timeframe_timestamp",
                table: "kline_cache",
                columns: new[] { "exchange_id", "pair", "timeframe", "timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_secrets_user_id",
                table: "mfa_secrets",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_client_order_id",
                table: "orders",
                column: "client_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_exchange_order_id",
                table: "orders",
                column: "exchange_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_status",
                table: "orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_orders_trader_id",
                table: "orders",
                column: "trader_id");

            migrationBuilder.CreateIndex(
                name: "ix_pairs_exchange_id_name",
                table: "pairs",
                columns: new[] { "exchange_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_exchange_id_pair_status",
                table: "positions",
                columns: new[] { "exchange_id", "pair", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_opening_order_id",
                table: "positions",
                column: "opening_order_id",
                unique: true,
                filter: "\"opening_order_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_positions_strategy_id_pair_status",
                table: "positions",
                columns: new[] { "strategy_id", "pair", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_trader_id_status",
                table: "positions",
                columns: new[] { "trader_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_recovery_codes_user_id_code",
                table: "recovery_codes",
                columns: new[] { "user_id", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_strategies_name",
                table: "strategies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strategy_bindings_status",
                table: "strategy_bindings",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_system_configs_key",
                table: "system_configs",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_traders_user_id_name",
                table: "traders",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "backtest_kline_analyses");

            migrationBuilder.DropTable(
                name: "backtest_results");

            migrationBuilder.DropTable(
                name: "backtest_tasks");

            migrationBuilder.DropTable(
                name: "exchange_order_histories");

            migrationBuilder.DropTable(
                name: "exchange_pair_rules");

            migrationBuilder.DropTable(
                name: "exchanges");

            migrationBuilder.DropTable(
                name: "kline_cache");

            migrationBuilder.DropTable(
                name: "mfa_secrets");

            migrationBuilder.DropTable(
                name: "notification_channels");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "pairs");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "recovery_codes");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "strategies");

            migrationBuilder.DropTable(
                name: "strategy_bindings");

            migrationBuilder.DropTable(
                name: "system_configs");

            migrationBuilder.DropTable(
                name: "traders");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
