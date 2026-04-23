using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Services.Implementations.Trading;

public class SignalMonitor(
    OnchainOSClient onchainOs,
    IOptions<BotSettings> settingsOptions,
    IStateManager stateManager,
    ILogger<SignalMonitor> logger) : ISignalMonitor
{
    private readonly BotSettings _settings = settingsOptions.Value;

    public async Task<IReadOnlyList<Signal>> FetchNewSignalsAsync(CancellationToken ct)
    {
        try
        {
            logger.LogDebug("获取{Chain}链聪明钱信号...", _settings.Signals.Chain);

            var signals = await onchainOs.GetSignalsAsync(_settings.Signals.Chain, ct);

            var state = await stateManager.LoadStateAsync(ct);
            var newSignals = signals.Where(s => !state.SeenSignals.ContainsKey(s.TokenAddress)).ToList();

            if (newSignals.Any())
            {
                logger.LogInformation("发现 {Count} 个新信号", newSignals.Count);

                foreach (var signal in newSignals)
                {
                    state.SeenSignals[signal.TokenAddress] = DateTime.UtcNow;
                }

                await stateManager.SaveStateAsync(state, ct);
            }

            return newSignals;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取信号失败");
            return [];
        }
    }

    public Task<IReadOnlyList<Signal>> FilterSignalsAsync(IReadOnlyList<Signal> signals, CancellationToken ct)
    {
        _ = ct;

        var filtered = signals.Where(signal =>
        {
            if (signal.MarketCap < _settings.Signals.MinMarketCap)
            {
                logger.LogDebug("信号过滤: {Token} 市值过低 ({MarketCap} < {Min})",
                    signal.TokenSymbol, signal.MarketCap, _settings.Signals.MinMarketCap);
                return false;
            }

            if (signal.Liquidity < _settings.Signals.MinLiquidity)
            {
                logger.LogDebug("信号过滤: {Token} 流动性不足 ({Liquidity} < {Min})",
                    signal.TokenSymbol, signal.Liquidity, _settings.Signals.MinLiquidity);
                return false;
            }

            if (signal.SoldRatio > _settings.Signals.MaxSoldRatio)
            {
                logger.LogDebug("信号过滤: {Token} 卖出比例过高 ({SoldRatio}% > {Max}%)",
                    signal.TokenSymbol, signal.SoldRatio, _settings.Signals.MaxSoldRatio);
                return false;
            }

            if (signal.SmartMoneyWallets < _settings.Signals.MinSmartMoneyWallets)
            {
                logger.LogDebug("信号过滤: {Token} 聪明钱包不足 ({Wallets} < {Min})",
                    signal.TokenSymbol, signal.SmartMoneyWallets, _settings.Signals.MinSmartMoneyWallets);
                return false;
            }

            logger.LogDebug("信号通过过滤: {Token} (市值: ${MarketCap}, 流动性: ${Liquidity})",
                signal.TokenSymbol, signal.MarketCap, signal.Liquidity);

            return true;
        }).ToList();

        return Task.FromResult<IReadOnlyList<Signal>>(filtered);
    }

    public async Task<IReadOnlyList<Signal>> SecurityScanAsync(IReadOnlyList<Signal> signals, CancellationToken ct)
    {
        List<Signal> safeSignals = [];

        foreach (var signal in signals)
        {
            try
            {
                var scanResult = await onchainOs.SecurityScanAsync(_settings.Signals.Chain, signal.TokenAddress, ct);

                signal.RiskLevel = scanResult.RiskLevel;
                signal.RiskLabels = scanResult.RiskLabels ?? [];

                if (_settings.Risk.RiskLevelBuyBlock.Contains(signal.RiskLevel))
                {
                    logger.LogWarning("安全扫描: {Token} 风险等级 {RiskLevel} - 跳过",
                        signal.TokenSymbol, signal.RiskLevel);
                    continue;
                }

                if (_settings.Risk.RiskLevelBuyPause.Contains(signal.RiskLevel))
                {
                    logger.LogWarning("安全扫描: {Token} 风险等级 {RiskLevel} - 需要人工确认",
                        signal.TokenSymbol, signal.RiskLevel);
                    continue;
                }

                safeSignals.Add(signal);
                logger.LogInformation("安全扫描通过: {Token} 风险等级: {RiskLevel}",
                    signal.TokenSymbol, scanResult.RiskLevel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "安全扫描失败: {Token}", signal.TokenSymbol);
            }
        }

        return safeSignals;
    }
}
