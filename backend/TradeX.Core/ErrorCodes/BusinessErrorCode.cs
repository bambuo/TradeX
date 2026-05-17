namespace TradeX.Core.ErrorCodes;

public enum BusinessErrorCode
{
    // 通用 1000-
    ValidationError = 1000,
    NotFound = 1001,
    Unauthenticated = 1002,
    Forbidden = 1003,
    Conflict = 1004,
    InternalError = 1005,

    // Auth 1100-
    AuthInvalidCredentials = 1100,
    AuthUserDisabled = 1101,
    AuthMfaRequired = 1102,
    AuthMfaInvalidCode = 1103,
    AuthMfaNotConfigured = 1104,
    AuthMfaReplayDetected = 1105,
    AuthMfaSecretInvalid = 1106,
    AuthRefreshTokenInvalid = 1107,
    AuthUsernameExists = 1108,
    AuthInvalidRole = 1109,

    // Setup 1150-
    SetupAlreadyInitialized = 1150,
    SetupInvalidInput = 1151,

    // Exchange 1200-
    ExchangeNotFound = 1200,
    ExchangeTestFailed = 1201,
    ExchangeRulesFetchFailed = 1202,
    ExchangeHasActiveStrategies = 1203,

    // Strategy 1300-
    StrategyNotFound = 1300,
    StrategyNotEditable = 1301,
    StrategyCannotEnable = 1302,
    StrategyExchangeConflict = 1303,
    StrategyIsActive = 1304,
    StrategyKlineWarmupFailed = 1305,

    // Order 1400-
    OrderNotFound = 1400,
    OrderSlippageExceeded = 1401,
    OrderRiskRejected = 1402,
    OrderInsufficientBalance = 1403,
    OrderExchangeRejected = 1404,

    // Backtest 1500-
    BacktestNotFound = 1500,
    BacktestInsufficientData = 1501,
    BacktestAlreadyRunning = 1502,

    // Trader 1600-
    TraderNotFound = 1600,
    TraderHasActiveStrategies = 1601,

    // Notification 1700-
    NotificationNotFound = 1700,
    NotificationTestFailed = 1701,
    NotificationInvalidConfig = 1702,

    // User 1800-
    UserNotFound = 1800,
    UserCannotDisableSuperAdmin = 1801,
    UserCannotDowngradeSuperAdmin = 1802,

    // System 1900-
    SystemNotInitialized = 1900,
    SystemEmergencyStop = 1901,
}
