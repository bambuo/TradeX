using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Services.Implementations.Trading;

public class PositionManager(
    OnchainOSClient onchainOs,
    IOptions<BotSettings> settingsOptions,
    IStateManager stateManager,
    ITradeExecutor tradeExecutor,
    ILogger<PositionManager> logger) : IPositionManager
{
    private readonly BotSettings _settings = settingsOptions.Value;

    public async Task UpdatePositionPricesAsync(CancellationToken ct)
    {
        try
        {
            var state = await stateManager.LoadStateAsync(ct);

            if (!state.OpenPositions.Any())
            {
                logger.LogDebug("没有持仓需要更新价格");
                return;
            }

            logger.LogDebug("更新 {Count} 个持仓价格", state.OpenPositions.Count);

            foreach (var position in state.OpenPositions.Values)
            {
                try
                {
                    var currentPrice = await onchainOs.GetTokenPriceAsync(_settings.Signals.Chain, position.TokenAddress, ct);

                    if (currentPrice > 0)
                    {
                        position.UpdatePrice(currentPrice);
                        logger.LogDebug("价格更新: {Token} ${Price} (持仓: {Quantity})", position.TokenSymbol, currentPrice, position.Quantity);
                    }
                    else
                    {
                        logger.LogWarning("获取价格失败: {Token}", position.TokenSymbol);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "更新持仓价格失败: {Token}", position.TokenSymbol);
                }
            }

            await stateManager.SaveStateAsync(state, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新持仓价格失败");
        }
    }

    public async Task ManagePositionsAsync(CancellationToken ct)
    {
        try
        {
            var state = await stateManager.LoadStateAsync(ct);

            if (!state.OpenPositions.Any())
            {
                logger.LogDebug("没有持仓需要管理");
                return;
            }

            logger.LogDebug("管理 {Count} 个持仓", state.OpenPositions.Count);

            foreach (var position in state.OpenPositions.Values.ToList())
            {
                try
                {
                    await tradeExecutor.CheckAndExecuteStopLossAsync(position, ct);
                    await tradeExecutor.CheckAndExecuteTakeProfitAsync(position, ct);

                    if (_settings.Risk.EnableTrailingStop)
                    {
                        var pnlPercent = position.EntryPriceUSD <= 0
                            ? 0
                            : ((position.CurrentPriceUSD - position.EntryPriceUSD) / position.EntryPriceUSD) * 100m;

                        if (!position.TrailingActive && pnlPercent >= _settings.Risk.MinProfitForTrailingPercent)
                        {
                            position.TrailingActive = true;
                            logger.LogInformation("激活动态移动止损: {Token} 盈利 {PnL:F2}%", position.TokenSymbol, pnlPercent);
                        }

                        if (position.TrailingActive)
                        {
                            var drawdownPercent = GetDynamicDrawdownPercent(pnlPercent);
                            var trailingStopPrice = position.MaxPriceUSD * (1 - drawdownPercent / 100m);

                            if (position.CurrentPriceUSD <= trailingStopPrice)
                            {
                                logger.LogWarning(
                                    "触发动态移动止损: {Token} 当前价: ${Current} ≤ 移动止损价: ${TrailingStop} (回撤 {Drawdown:F2}%)",
                                    position.TokenSymbol,
                                    position.CurrentPriceUSD,
                                    trailingStopPrice,
                                    drawdownPercent);

                                await tradeExecutor.ExecuteSellAsync(position, $"动态移动止损 ({drawdownPercent:F2}%)", ct);
                            }
                        }
                    }

                    if (position.NetPnLPercent != 0)
                    {
                        var status = position.NetPnLPercent > 0 ? "盈利" : "亏损";
                        logger.LogDebug("持仓状态: {Token} {Status} {PnLPercent:F2}% (${PnLUSD:F2})", position.TokenSymbol, status, position.NetPnLPercent, position.NetPnLUSD);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "管理持仓失败: {Token}", position.TokenSymbol);
                }
            }

            await stateManager.SaveStateAsync(state, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "管理持仓失败");
        }
    }

    public async Task<TimeSpan> GetAdjustedPollIntervalAsync(CancellationToken ct)
    {
        try
        {
            var state = await stateManager.LoadStateAsync(ct);

            if (!state.OpenPositions.Any())
            {
                return TimeSpan.FromSeconds(_settings.Monitoring.PollIntervalSeconds);
            }

            var hasHighVol = state.OpenPositions.Values.Any(position =>
            {
                var priceChangePercent = Math.Abs((position.CurrentPriceUSD - position.EntryPriceUSD) / Math.Max(position.EntryPriceUSD, 0.00000001m) * 100);
                var threshold = position.EntryPriceUSD < 0.01m
                    ? _settings.Monitoring.MicrocapHighVolThresholdPercent
                    : _settings.Monitoring.HighVolThresholdPercent;

                return priceChangePercent >= threshold;
            });

            if (hasHighVol)
            {
                logger.LogDebug("检测到高波动持仓，缩短轮询间隔");
                return TimeSpan.FromSeconds(_settings.Monitoring.HighVolIntervalSeconds);
            }

            return TimeSpan.FromSeconds(_settings.Monitoring.PollIntervalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取轮询间隔失败，使用默认值");
            return TimeSpan.FromSeconds(_settings.Monitoring.PollIntervalSeconds);
        }
    }

    private decimal GetDynamicDrawdownPercent(decimal pnlPercent)
    {
        var baseDrawdown = _settings.Risk.TrailingStopPercent;
        if (!_settings.Risk.DynamicTrailingEnabled)
        {
            return baseDrawdown;
        }

        decimal drawdown = pnlPercent switch
        {
            > 30m => baseDrawdown * 1.5m,
            > 20m => baseDrawdown * 1.3m,
            > 15m => baseDrawdown * 1.1m,
            _ => baseDrawdown
        };

        return Math.Min(drawdown, 20m);
    }
}
