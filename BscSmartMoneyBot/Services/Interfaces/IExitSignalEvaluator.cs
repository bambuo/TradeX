using BscSmartMoneyBot.Core.Models;

namespace BscSmartMoneyBot.Services.Interfaces;

public interface IExitSignalEvaluator
{
    bool ShouldStopLoss(Position position, out decimal stopLossPrice);
    PartialTakeProfitDecision? GetPartialTakeProfitDecision(Position position);
    bool ShouldTakeProfit(Position position, out decimal takeProfitPrice);
}

public sealed record PartialTakeProfitDecision(int TargetIndex, decimal SellRatio, string Reason);
