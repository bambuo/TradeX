using BscSmartMoneyBot.Core.Models;

namespace BscSmartMoneyBot.Services.Interfaces;

public interface ITradeExecutor
{
    decimal GetRecommendedBuyAmount(Signal signal, decimal? accountBalanceUsd = null);
    decimal CalculateSmartSlippagePercent(Signal signal, decimal tradeAmountUsd, bool isSell);
    Task<bool> ExecuteBuyAsync(Signal signal, decimal amountUSD, CancellationToken ct);
    Task<bool> ExecuteSellAsync(Position position, string reason, CancellationToken ct);
    Task<bool> ExecutePartialSellAsync(Position position, decimal sellRatio, string reason, CancellationToken ct);
    Task CheckAndExecuteStopLossAsync(Position position, CancellationToken ct);
    Task CheckAndExecuteTakeProfitAsync(Position position, CancellationToken ct);
}
