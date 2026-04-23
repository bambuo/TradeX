using BscSmartMoneyBot.Commands;
using BscSmartMoneyBot.HostedServices;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Implementations.Persistence;
using BscSmartMoneyBot.Services.Implementations.Trading;
using BscSmartMoneyBot.Services.Implementations.Trading.Strategies;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BscSmartMoneyBot.Configuration;

public static class HostBuilderExtensions
{
    private const string EnvironmentVariablePrefix = "BSCBOT_";
    private const string DefaultLogFilePath = "logs/bot.log";

    public static IHostBuilder ConfigureBotConfiguration(this IHostBuilder hostBuilder, CommandLineOptions options, string[] args)
    {
        return hostBuilder.ConfigureAppConfiguration((context, config) =>
        {
            config.Sources.Clear();

            config.AddJsonFile(options.ConfigPath, optional: false, reloadOnChange: true);

            var environmentName = context.HostingEnvironment.EnvironmentName;
            config.AddJsonFile($"appsettings.{environmentName}.json", optional: true);

            config.AddEnvironmentVariables(EnvironmentVariablePrefix);
            config.AddCommandLine(args);
        });
    }

    public static IHostBuilder ConfigureBotServices(this IHostBuilder hostBuilder, CommandLineOptions options)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.Configure<BotSettings>(context.Configuration.GetSection(nameof(BotSettings)));
            services.PostConfigure<BotSettings>(settings => ApplyCommandLineOverrides(settings, options));

            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSerilog();
            });

            services.AddSingleton<OnchainOSClient>();
            services.AddSingleton<IStateManager, StateManager>();
            services.AddSingleton<ISignalMonitor, SignalMonitor>();
            services.AddSingleton<IPositionManager, PositionManager>();
            services.AddSingleton<IPositionSizingStrategy, PositionSizingStrategy>();
            services.AddSingleton<ISlippageStrategy, SmartSlippageStrategy>();
            services.AddSingleton<IExitSignalEvaluator, ExitSignalEvaluator>();
            services.AddSingleton<ITradeExecutor, TradeExecutor>();

            services.AddHostedService<TradingBotHostedService>();
        });
    }

    public static IHostBuilder ConfigureBotSerilog(this IHostBuilder hostBuilder, CommandLineOptions options)
    {
        return hostBuilder.UseSerilog((context, _, configuration) =>
        {
            var logLevel = options.LogLevel;
            var logFilePath = context.Configuration["BotSettings:LogFilePath"] ?? DefaultLogFilePath;
            var minimumLevel = options.Verbose
                ? Serilog.Events.LogEventLevel.Debug
                : Enum.Parse<Serilog.Events.LogEventLevel>(logLevel);

            configuration
                .MinimumLevel.Is(minimumLevel)
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        });
    }

    private static void ApplyCommandLineOverrides(BotSettings settings, CommandLineOptions options)
    {
        if (options.DryRun)
        {
            settings.DryRun = true;
        }

        if (!string.IsNullOrEmpty(options.StatePath))
        {
            settings.StateFilePath = options.StatePath;
        }

        if (options.PollInterval.HasValue)
        {
            settings.Monitoring.PollIntervalSeconds = options.PollInterval.Value;
        }

        if (options.MaxPositions.HasValue)
        {
            settings.Trading.MaxOpenPositions = options.MaxPositions.Value;
        }

        if (!string.IsNullOrEmpty(options.WalletAddress))
        {
            settings.Wallet.Address = options.WalletAddress;
        }

        if (!string.IsNullOrEmpty(options.Chain))
        {
            settings.Signals.Chain = options.Chain;
        }
    }
}
