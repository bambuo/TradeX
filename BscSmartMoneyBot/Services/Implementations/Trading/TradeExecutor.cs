using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Services.Implementations.Trading;

public class TradeExecutor(
    OnchainOSClient onchainOS,
    IOptions<BotSettings> settings,
    IStateManager stateManager,
    IPositionSizingStrategy positionSizingStrategy,
    ISlippageStrategy slippageStrategy,
    IExitSignalEvaluator exitSignalEvaluator,
    ILogger<TradeExecutor> logger) : ITradeExecutor
{
    private const string UsdtTokenAddress = "0x55d398326f99059ff775485246999027b3197955";
    private const decimal DryRunBuyFeeRatio = 0.0015m;
    private const decimal MinPriceGuard = 0.00000001m;

    private readonly BotSettings _settings = settings.Value;

    public decimal GetRecommendedBuyAmount(Signal signal, decimal? accountBalanceUsd = null)
    {
        signal.Score = positionSizingStrategy.CalculateSignalScore(signal);
        return positionSizingStrategy.GetRecommendedBuyAmount(signal, accountBalanceUsd);
    }

    public decimal CalculateSmartSlippagePercent(Signal signal, decimal tradeAmountUsd, bool isSell) =>
        slippageStrategy.CalculateSmartSlippagePercent(signal, tradeAmountUsd, isSell);

    public async Task<bool> ExecuteBuyAsync(Signal signal, decimal amountUSD, CancellationToken ct)
    {
        try
        {
            var state = await stateManager.LoadStateAsync(ct);

            if (IsInCooldown(state.LastBuyTime))
            {
                var cooldownEnd = state.LastBuyTime!.Value.AddMinutes(_settings.Trading.CooldownMinutes);
                logger.LogInformation("冷却时间中，跳过买入: {Token} (冷却结束: {CooldownEnd})", signal.TokenSymbol, cooldownEnd);
                return false;
            }

            if (state.OpenPositions.Count >= _settings.Trading.MaxOpenPositions)
            {
                logger.LogInformation("已达到最大持仓数 ({Max})，跳过买入: {Token}", _settings.Trading.MaxOpenPositions, signal.TokenSymbol);
                return false;
            }

            if (_settings.Wallet.CheckBalanceBeforeTrade)
            {
                var usdtBalance = await onchainOS.GetTokenBalanceAsync(_settings.Signals.Chain, UsdtTokenAddress, _settings.Wallet.Address, ct);
                if (usdtBalance < amountUSD)
                {
                    logger.LogWarning("USDT余额不足: ${Balance} < ${Amount}，跳过买入: {Token}", usdtBalance, amountUSD, signal.TokenSymbol);
                    return false;
                }
            }

            var buySlippage = CalculateSmartSlippagePercent(signal, amountUSD, isSell: false);
            logger.LogInformation("执行买入: {Token} ${Amount} (score={Score:F2}, slippage={Slippage}%)", signal.TokenSymbol, amountUSD, signal.Score, buySlippage);

            if (_settings.DryRun)
            {
                return await ExecuteDryRunBuyAsync(signal, amountUSD, state, ct);
            }

            return await ExecuteRealBuyAsync(signal, amountUSD, buySlippage, state, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "买入失败: {Token}", signal.TokenSymbol);
            return false;
        }
    }

    public async Task<bool> ExecuteSellAsync(Position position, string reason, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("执行卖出: {Token} ({Reason})", position.TokenSymbol, reason);

            if (_settings.DryRun)
            {
                return await ExecuteDryRunSellAsync(position, ct);
            }

            var signal = CreateExitSignal(position);
            var sellSlippage = CalculateSmartSlippagePercent(signal, position.CurrentValueUSD, isSell: true);

            var swapResult = await onchainOS.ExecuteSwapAsync(
                chain: _settings.Signals.Chain,
                fromToken: position.TokenAddress,
                toToken: UsdtTokenAddress,
                amount: position.Quantity,
                wallet: _settings.Wallet.Address,
                slippage: sellSlippage,
                ct);

            var txDetail = await onchainOS.GetTransactionDetailAsync(_settings.Signals.Chain, swapResult.SwapTxHash, ct);
            var state = await stateManager.LoadStateAsync(ct);

            var sellAmountUSD = swapResult.ToAmount;
            var actualSellFeeUSD = txDetail.ServiceChargeUsd > 0 ? txDetail.ServiceChargeUsd : _settings.Fee.FallbackFeeUSD;
            var netPnl = sellAmountUSD - position.BuyCostUSD - position.BuyFeeUSD - actualSellFeeUSD;

            if (netPnl > 0)
            {
                state.TotalProfitUSD += netPnl;
            }
            else
            {
                state.TotalLossUSD += Math.Abs(netPnl);
            }

            state.TotalSells++;
            state.OpenPositions.Remove(position.Id);
            await stateManager.SaveStateAsync(state, ct);

            logger.LogInformation("✅ 卖出成功: {Token} 收益: ${PnL} (原因: {Reason})", position.TokenSymbol, netPnl, reason);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "卖出失败: {Token}", position.TokenSymbol);
            return false;
        }
    }

    public async Task<bool> ExecutePartialSellAsync(Position position, decimal sellRatio, string reason, CancellationToken ct)
    {
        if (sellRatio <= 0 || sellRatio >= 1)
        {
            logger.LogWarning("无效的分批卖出比例: {Ratio}", sellRatio);
            return false;
        }

        var qtyToSell = Math.Round(position.Quantity * sellRatio, 8);
        if (qtyToSell <= 0)
        {
            return false;
        }

        try
        {
            if (_settings.DryRun)
            {
                return await ExecuteDryRunPartialSellAsync(position, qtyToSell, sellRatio, reason, ct);
            }

            var signal = CreateExitSignal(position);
            var sellSlippage = CalculateSmartSlippagePercent(signal, position.CurrentValueUSD * sellRatio, isSell: true);

            await onchainOS.ExecuteSwapAsync(
                chain: _settings.Signals.Chain,
                fromToken: position.TokenAddress,
                toToken: UsdtTokenAddress,
                amount: qtyToSell,
                wallet: _settings.Wallet.Address,
                slippage: sellSlippage,
                ct);

            var state = await stateManager.LoadStateAsync(ct);
            if (!state.OpenPositions.TryGetValue(position.Id, out var updated))
            {
                return false;
            }

            updated.Quantity = Math.Max(0, updated.Quantity - qtyToSell);
            updated.BuyCostUSD = Math.Max(0, updated.BuyCostUSD * (1 - sellRatio));
            updated.EstSellFeeUSD = EstimateSellFeeUsd(updated.CurrentValueUSD, updated.CurrentPriceUSD);

            if (updated.Quantity <= 0)
            {
                state.OpenPositions.Remove(position.Id);
                state.TotalSells++;
            }

            await stateManager.SaveStateAsync(state, ct);
            logger.LogInformation("✅ 分批卖出成功: {Token} {Ratio:P0} 原因: {Reason}", position.TokenSymbol, sellRatio, reason);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "分批卖出失败: {Token}", position.TokenSymbol);
            return false;
        }
    }

    public async Task CheckAndExecuteStopLossAsync(Position position, CancellationToken ct)
    {
        if (!exitSignalEvaluator.ShouldStopLoss(position, out var stopLossPrice))
        {
            return;
        }

        logger.LogWarning("触发止损: {Token} 当前价: ${Current} ≤ 止损价: ${StopLoss}", position.TokenSymbol, position.CurrentPriceUSD, stopLossPrice);
        await ExecuteSellAsync(position, $"止损 ({_settings.Risk.StopLossPercent}%)", ct);
    }

    public async Task CheckAndExecuteTakeProfitAsync(Position position, CancellationToken ct)
    {
        var partialDecision = exitSignalEvaluator.GetPartialTakeProfitDecision(position);
        if (partialDecision is not null)
        {
            var success = await ExecutePartialSellAsync(position, partialDecision.SellRatio, partialDecision.Reason, ct);
            if (success)
            {
                position.TpTaken.Add(partialDecision.TargetIndex);
            }

            return;
        }

        if (!exitSignalEvaluator.ShouldTakeProfit(position, out var takeProfitPrice))
        {
            return;
        }

        logger.LogWarning("触发止盈: {Token} 当前价: ${Current} ≥ 止盈价: ${TakeProfit}", position.TokenSymbol, position.CurrentPriceUSD, takeProfitPrice);
        await ExecuteSellAsync(position, $"止盈 ({_settings.Risk.TakeProfitPercent}%)", ct);
    }

    private async Task<bool> ExecuteDryRunBuyAsync(Signal signal, decimal amountUSD, BotState state, CancellationToken ct)
    {
        logger.LogInformation("🔶 测试模式 - 模拟买入 {Token} ${Amount}", signal.TokenSymbol, amountUSD);

        var fee = EstimateSellFeeUsd(amountUSD, signal.PriceUSD);
        var position = new Position
        {
            TokenAddress = signal.TokenAddress,
            TokenSymbol = signal.TokenSymbol,
            Quantity = amountUSD / Math.Max(signal.PriceUSD, MinPriceGuard),
            EntryPriceUSD = signal.PriceUSD,
            BuyCostUSD = amountUSD,
            BuyFeeUSD = amountUSD * DryRunBuyFeeRatio,
            EstSellFeeUSD = fee,
            MaxPriceUSD = signal.PriceUSD,
            BuyTxHash = $"DRYRUN_{Guid.NewGuid()}",
            BuyTime = DateTime.UtcNow,
            TpTaken = []
        };

        state.OpenPositions[position.Id] = position;
        state.LastBuyTime = DateTime.UtcNow;
        state.TotalBuys++;
        await stateManager.SaveStateAsync(state, ct);
        return true;
    }

    private async Task<bool> ExecuteRealBuyAsync(Signal signal, decimal amountUSD, decimal buySlippage, BotState state, CancellationToken ct)
    {
        var swapResult = await onchainOS.ExecuteSwapAsync(
            chain: _settings.Signals.Chain,
            fromToken: UsdtTokenAddress,
            toToken: signal.TokenAddress,
            amount: amountUSD,
            wallet: _settings.Wallet.Address,
            slippage: buySlippage,
            ct);

        var txDetail = await onchainOS.GetTransactionDetailAsync(_settings.Signals.Chain, swapResult.SwapTxHash, ct);

        var quantity = Math.Max(swapResult.ToAmount, 0m);
        var buyCostUSD = Math.Max(swapResult.FromAmount, amountUSD);
        var buyFeeUSD = txDetail.ServiceChargeUsd > 0 ? txDetail.ServiceChargeUsd : _settings.Fee.FallbackFeeUSD;
        var estSellFeeUSD = EstimateSellFeeUsd(buyCostUSD, signal.PriceUSD);
        var entryPrice = quantity > 0 ? buyCostUSD / quantity : signal.PriceUSD;

        var position = new Position
        {
            TokenAddress = signal.TokenAddress,
            TokenSymbol = signal.TokenSymbol,
            Quantity = quantity,
            EntryPriceUSD = entryPrice,
            BuyCostUSD = buyCostUSD,
            BuyFeeUSD = buyFeeUSD,
            EstSellFeeUSD = estSellFeeUSD,
            MaxPriceUSD = entryPrice,
            BuyTxHash = swapResult.SwapTxHash,
            BuyTime = DateTime.UtcNow,
            TpTaken = []
        };

        state.OpenPositions[position.Id] = position;
        state.LastBuyTime = DateTime.UtcNow;
        state.TotalBuys++;
        await stateManager.SaveStateAsync(state, ct);

        logger.LogInformation("✅ 买入成功: {Token} {Quantity} (成本: ${Cost}, 手续费: ${Fee})", signal.TokenSymbol, quantity, buyCostUSD, buyFeeUSD);
        return true;
    }

    private async Task<bool> ExecuteDryRunSellAsync(Position position, CancellationToken ct)
    {
        logger.LogInformation("🔶 测试模式 - 模拟卖出 {Token}", position.TokenSymbol);

        var state = await stateManager.LoadStateAsync(ct);
        var netPnl = position.NetPnLUSD;
        if (netPnl > 0)
        {
            state.TotalProfitUSD += netPnl;
        }
        else
        {
            state.TotalLossUSD += Math.Abs(netPnl);
        }

        state.TotalSells++;
        state.OpenPositions.Remove(position.Id);
        await stateManager.SaveStateAsync(state, ct);
        return true;
    }

    private async Task<bool> ExecuteDryRunPartialSellAsync(Position position, decimal qtyToSell, decimal sellRatio, string reason, CancellationToken ct)
    {
        var state = await stateManager.LoadStateAsync(ct);
        if (!state.OpenPositions.TryGetValue(position.Id, out var current))
        {
            return false;
        }

        current.Quantity = Math.Max(0, current.Quantity - qtyToSell);
        current.BuyCostUSD = Math.Max(0, current.BuyCostUSD * (1 - sellRatio));
        current.EstSellFeeUSD = EstimateSellFeeUsd(current.CurrentValueUSD, current.CurrentPriceUSD);

        if (current.Quantity <= 0)
        {
            state.OpenPositions.Remove(position.Id);
            state.TotalSells++;
        }

        await stateManager.SaveStateAsync(state, ct);
        logger.LogInformation("🔶 测试模式 - 分批卖出 {Token}: {Ratio:P0}, 数量 {Qty}, 原因: {Reason}", position.TokenSymbol, sellRatio, qtyToSell, reason);
        return true;
    }

    private Signal CreateExitSignal(Position position) =>
        new()
        {
            TokenAddress = position.TokenAddress,
            TokenSymbol = position.TokenSymbol,
            MarketCap = 0,
            Liquidity = Math.Max(position.CurrentValueUSD, 1),
            SoldRatio = 0,
            PriceUSD = Math.Max(position.CurrentPriceUSD, position.EntryPriceUSD)
        };

    private bool IsInCooldown(DateTime? lastBuyTime)
    {
        if (!lastBuyTime.HasValue)
        {
            return false;
        }

        var cooldownEnd = lastBuyTime.Value.AddMinutes(_settings.Trading.CooldownMinutes);
        return DateTime.UtcNow < cooldownEnd;
    }

    private decimal EstimateSellFeeUsd(decimal positionValueUsd, decimal currentPrice)
    {
        const decimal minFee = 0.005m;
        var maxFee = Math.Max(minFee, positionValueUsd * 0.05m);

        var networkFee = currentPrice <= 0 ? _settings.Fee.FallbackFeeUSD : 0.02m;
        var mevFee = _settings.Fee.MevProtection ? 0.0001m : 0m;
        var estimated = networkFee + mevFee;

        return Math.Clamp(estimated, minFee, maxFee);
    }
}
