namespace BscSmartMoneyBot.Configuration;

public class BotSettings
{
    public string Name { get; set; } = "BSC Smart Money Trading Bot";
    public string Version { get; set; } = "1.0.0";
    public bool DryRun { get; set; } = true;
    public bool Debug { get; set; } = false;
    public string StateFilePath { get; set; } = "state/bot_state.json";
    public string LogFilePath { get; set; } = "logs/bot.log";
    public string BackupDirectory { get; set; } = "state/backup";

    public MonitoringSettings Monitoring { get; set; } = new();
    public SignalSettings Signals { get; set; } = new();
    public TradingSettings Trading { get; set; } = new();
    public RiskSettings Risk { get; set; } = new();
    public WalletSettings Wallet { get; set; } = new();
    public OnchainOSSettings OnchainOS { get; set; } = new();
    public FeeSettings Fee { get; set; } = new();
    public TakeProfitSettings TakeProfit { get; set; } = new();
    public SlippageSettings Slippage { get; set; } = new();
    public AdaptiveSettings Adaptive { get; set; } = new();
}

public class MonitoringSettings
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int HighVolIntervalSeconds { get; set; } = 3;
    public int PriceCheckIntervalSeconds { get; set; } = 10;
    public decimal HighVolThresholdPercent { get; set; } = 3.0m;
    public decimal MicrocapHighVolThresholdPercent { get; set; } = 5.0m;
}

public class SignalSettings
{
    public string Chain { get; set; } = "bsc";
    public decimal MinMarketCap { get; set; } = 50000;
    public decimal MinLiquidity { get; set; } = 100000;
    public decimal MaxSoldRatio { get; set; } = 85;
    public int MinSmartMoneyWallets { get; set; } = 3;
    public bool ExcludeHoneypot { get; set; } = true;
    public bool ExcludeNewTokens { get; set; } = false;
    public int MaxTokenAgeHours { get; set; } = 24;
}

public class TradingSettings
{
    public int MaxOpenPositions { get; set; } = 1;
    public int CooldownMinutes { get; set; } = 30;
    public decimal DefaultSlippage { get; set; } = 0.25m;
    public string GasLevel { get; set; } = "fast";
    public decimal MaxAutoSlippage { get; set; } = 0.25m;
    public decimal MaxPositionSizeUSD { get; set; } = 50;
    public decimal MinPositionSizeUSD { get; set; } = 5;
}

public class RiskSettings
{
    public decimal StopLossPercent { get; set; } = 10.0m;
    public decimal TakeProfitPercent { get; set; } = 50.0m;
    public decimal TrailingStopPercent { get; set; } = 5.0m;
    public bool EnableTrailingStop { get; set; } = true;
    public bool IncludeFeesInPnL { get; set; } = true;
    public decimal MinProfitForTrailingPercent { get; set; } = 15.0m;
    public bool DynamicTrailingEnabled { get; set; } = true;
    public List<string> RiskLevelBuyBlock { get; set; } = ["CRITICAL"];
    public List<string> RiskLevelBuyPause { get; set; } = ["HIGH"];
}

public class WalletSettings
{
    public string Address { get; set; } = string.Empty;
    public decimal MinBalanceUSDT { get; set; } = 10.0m;
    public decimal MinBalanceBNB { get; set; } = 0.01m;
    public bool CheckBalanceBeforeTrade { get; set; } = true;
}

public class OnchainOSSettings
{
    public string BinaryPath { get; set; } = "onchainos";
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
}

public class FeeSettings
{
    public bool UseRealTimeFees { get; set; } = true;
    public int CacheDurationSeconds { get; set; } = 300;
    public decimal GasRefreshThresholdPercent { get; set; } = 5.0m;
    public int MinGasRefreshIntervalSeconds { get; set; } = 60;
    public bool MevProtection { get; set; } = true;
    public decimal FallbackFeeUSD { get; set; } = 0.02m;
}

public class TakeProfitSettings
{
    public bool PartialTakeProfitEnabled { get; set; } = true;
    public List<decimal> TargetsPercent { get; set; } = [20m, 35m, 50m];
    public List<decimal> SellRatios { get; set; } = [20m, 30m, 50m];
}

public class SlippageSettings
{
    public bool SmartSlippageEnabled { get; set; } = true;
    public decimal MinSlippagePercent { get; set; } = 0.10m;
    public decimal MaxSlippagePercent { get; set; } = 0.25m;
    public decimal BaseBuySlippagePercent { get; set; } = 0.15m;
    public decimal BaseSellSlippagePercent { get; set; } = 0.15m;
    public decimal LiquidityThresholdUSD { get; set; } = 100000m;
}

public class AdaptiveSettings
{
    public bool DynamicPositionEnabled { get; set; } = true;
    public decimal MaxRiskPerTradePercent { get; set; } = 1.0m;
    public bool PositionScalingEnabled { get; set; } = true;
}
