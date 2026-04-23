using BscSmartMoneyBot.Core.Models;

namespace BscSmartMoneyBot.Services.Interfaces;

public interface ISlippageStrategy
{
    decimal CalculateSmartSlippagePercent(Signal signal, decimal tradeAmountUsd, bool isSell);
}
