using BscSmartMoneyBot.Commands;
using BscSmartMoneyBot.Configuration;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace BscSmartMoneyBot;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // 解析命令行参数
        var parserResult = Parser.Default.ParseArguments<CommandLineOptions>(args);
        
        await parserResult.MapResult(
            async options => await RunBotAsync(options, args),
            errors => Task.FromResult(1)
        );
    }
    
    private static async Task RunBotAsync(CommandLineOptions options, string[] args)
    {
        try
        {
            WriteBanner();

            var host = Host.CreateDefaultBuilder()
                .ConfigureBotConfiguration(options, args)
                .ConfigureBotServices(options)
                .ConfigureBotSerilog(options)
                .Build();

            PrintStartupSummary(host.Services.GetRequiredService<IOptions<BotSettings>>().Value);
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 启动失败: {ex.Message}");
            Log.Fatal(ex, "机器人启动失败");
            Environment.Exit(1);
        }
    }

    private static void WriteBanner()
    {
        Console.WriteLine("""
                          ┌─────────────────────────────────────────────┐
                          │    BSC Smart Money Trading Bot v1.0.0       │
                          │    https://web3.okx.com/onchainos           │
                          └─────────────────────────────────────────────┘
                          """);
    }

    private static void PrintStartupSummary(BotSettings botSettings)
    {
        var walletDisplay = string.IsNullOrEmpty(botSettings.Wallet.Address)
            ? "未配置"
            : botSettings.Wallet.Address.Length <= 10
                ? botSettings.Wallet.Address
                : botSettings.Wallet.Address[..10] + "...";

        Console.WriteLine($"模式: {(botSettings.DryRun ? "🔶 测试模式" : "🚀 实盘模式")}");
        Console.WriteLine($"链: {botSettings.Signals.Chain}");
        Console.WriteLine($"钱包: {walletDisplay}");
        Console.WriteLine($"轮询间隔: {botSettings.Monitoring.PollIntervalSeconds}秒");
        Console.WriteLine($"最大持仓: {botSettings.Trading.MaxOpenPositions}");
        Console.WriteLine("─".PadRight(50, '─'));
    }
}
