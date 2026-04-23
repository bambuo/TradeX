using BscSmartMoneyBot.Core.Models;

namespace BscSmartMoneyBot.Services.Interfaces;

public interface IPositionSizingStrategy
{
    decimal CalculateSignalScore(Signal signal);
    decimal GetRecommendedBuyAmount(Signal signal, decimal? accountBalanceUsd = null);
}
