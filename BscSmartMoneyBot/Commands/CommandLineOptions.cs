using CommandLine;

namespace BscSmartMoneyBot.Commands;

public class CommandLineOptions
{
    [Option('c', "config", Required = false, HelpText = "配置文件路径", Default = "appsettings.json")]
    public string ConfigPath { get; set; } = "appsettings.json";
        
    [Option('d', "dry-run", Required = false, HelpText = "测试模式（不执行实际交易）", Default = false)]
    public bool DryRun { get; set; }
        
    [Option('v', "verbose", Required = false, HelpText = "详细日志输出", Default = false)]
    public bool Verbose { get; set; }
        
    [Option('l', "log-level", Required = false, HelpText = "日志级别 (Debug, Information, Warning, Error)", Default = "Information")]
    public string LogLevel { get; set; } = "Information";
        
    [Option('s', "state", Required = false, HelpText = "状态文件路径")]
    public string? StatePath { get; set; }
        
    [Option('i', "interval", Required = false, HelpText = "轮询间隔（秒）")]
    public int? PollInterval { get; set; }
        
    [Option("max-positions", Required = false, HelpText = "最大持仓数量")]
    public int? MaxPositions { get; set; }
        
    [Option("wallet", Required = false, HelpText = "钱包地址")]
    public string? WalletAddress { get; set; }
        
    [Option("chain", Required = false, HelpText = "区块链网络 (bsc, ethereum等)")]
    public string? Chain { get; set; }
}