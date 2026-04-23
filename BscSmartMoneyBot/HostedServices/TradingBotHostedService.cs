using System.Diagnostics;
using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.HostedServices;

public class TradingBotHostedService(
    ILogger<TradingBotHostedService> logger,
    ISignalMonitor signalMonitor,
    IPositionManager positionManager,
    ITradeExecutor tradeExecutor,
    IStateManager stateManager,
    IOptions<BotSettings> settings) : BackgroundService
{
    private readonly BotSettings _settings = settings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("🚀 BSC 聪明钱交易机器人启动");
        logger.LogInformation("模式: {Mode}", _settings.DryRun ? "🔶 测试模式" : "🚀 实盘模式");
        logger.LogInformation("链: {Chain}", _settings.Signals.Chain);
        logger.LogInformation("钱包: {Wallet}",
            string.IsNullOrEmpty(_settings.Wallet.Address) ? "未配置" :
            _settings.Wallet.Address.Length <= 10 ? _settings.Wallet.Address :
            _settings.Wallet.Address[..10] + "...");
        logger.LogInformation("轮询间隔: {Interval}秒", _settings.Monitoring.PollIntervalSeconds);
        logger.LogInformation("最大持仓: {MaxPositions}", _settings.Trading.MaxOpenPositions);
        logger.LogInformation("─".PadRight(50, '─'));

        // 初始加载状态
        var state = await stateManager.LoadStateAsync(stoppingToken);
        logger.LogInformation("📊 当前状态: {Positions}个持仓, {Signals}个已见信号",
                state.OpenPositions.Count, state.SeenSignals.Count);

        // 显示持仓摘要
        if (state.OpenPositions.Any())
        {
            logger.LogInformation("当前持仓:");
            foreach (var position in state.OpenPositions.Values)
            {
                var status = position.NetPnLPercent > 0 ? "📈" : "📉";
                logger.LogInformation("  {Status} {Token}: {Quantity} (成本: ${Cost}, 当前: ${Current}, PnL: {PnLPercent:F2}%)",
                    status, position.TokenSymbol, position.Quantity,
                    position.BuyCostUSD, position.CurrentValueUSD, position.NetPnLPercent);
            }
        }

        // 创建状态备份
        await stateManager.BackupStateAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                logger.LogDebug("开始新轮询周期");

                // 1. 更新持仓价格
                await positionManager.UpdatePositionPricesAsync(stoppingToken);

                // 2. 获取新信号
                var newSignals = await signalMonitor.FetchNewSignalsAsync(stoppingToken);

                if (newSignals.Any())
                {
                    logger.LogInformation("发现 {Count} 个新信号", newSignals.Count);

                    // 3. 过滤信号
                    var filtered = await signalMonitor.FilterSignalsAsync(newSignals, stoppingToken);

                    if (filtered.Any())
                    {
                        logger.LogInformation("{Count} 个信号通过基础过滤", filtered.Count);

                        // 4. 安全扫描
                        var safeTokens = await signalMonitor.SecurityScanAsync(filtered, stoppingToken);

                        if (safeTokens.Any())
                        {
                            logger.LogInformation("{Count} 个信号通过安全扫描", safeTokens.Count);

                            // 5. 执行买入
                            foreach (var signal in safeTokens)
                            {
                                state = await stateManager.LoadStateAsync(stoppingToken);
                                if (state.OpenPositions.Count >= _settings.Trading.MaxOpenPositions)
                                {
                                    logger.LogInformation("已达到最大持仓数，停止买入");
                                    break;
                                }

                                var buyAmount = tradeExecutor.GetRecommendedBuyAmount(signal);

                                logger.LogInformation("尝试买入: {Token} ${Amount} (score={Score:F2})",
                                    signal.TokenSymbol, buyAmount, signal.Score);

                                var success = await tradeExecutor.ExecuteBuyAsync(signal, buyAmount, stoppingToken);

                                if (success)
                                {
                                    logger.LogInformation("买入成功: {Token}", signal.TokenSymbol);
                                }
                            }
                        }
                    }
                }

                // 6. 管理持仓（止盈止损）
                await positionManager.ManagePositionsAsync(stoppingToken);

                // 7. 动态调整轮询间隔
                var interval = await positionManager.GetAdjustedPollIntervalAsync(stoppingToken);

                stopwatch.Stop();
                logger.LogDebug("周期完成，耗时: {Elapsed}ms，下一轮间隔: {Interval}s",
                    stopwatch.ElapsedMilliseconds, interval.TotalSeconds);

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("服务停止请求收到");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "执行周期出错");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        // 保存最终状态
        await stateManager.SaveStateAsync(state, stoppingToken);
        logger.LogInformation("🛑 交易机器人停止");
    }
}
