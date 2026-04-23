using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Services.Implementations.Trading.Strategies;

public class ExitSignalEvaluator(IOptions<BotSettings> settingsOptions) : IExitSignalEvaluator
{
    private readonly BotSettings _settings = settingsOptions.Value;

    public bool ShouldStopLoss(Position position, out decimal stopLossPrice)
    {
        stopLossPrice = position.EntryPriceUSD * (1 - _settings.Risk.StopLossPercent / 100m);
        return position.CurrentPriceUSD <= stopLossPrice;
    }

    public PartialTakeProfitDecision? GetPartialTakeProfitDecision(Position position)
    {
        if (!_settings.TakeProfit.PartialTakeProfitEnabled)
        {
            return null;
        }

        if (_settings.TakeProfit.TargetsPercent.Count != _settings.TakeProfit.SellRatios.Count)
        {
            return null;
        }

        var pnlPct = position.EntryPriceUSD <= 0
            ? 0
            : ((position.CurrentPriceUSD - position.EntryPriceUSD) / position.EntryPriceUSD) * 100m;

        for (var i = 0; i < _settings.TakeProfit.TargetsPercent.Count; i++)
        {
            if (position.TpTaken.Contains(i))
            {
                continue;
            }

            if (pnlPct < _settings.TakeProfit.TargetsPercent[i])
            {
                continue;
            }

            var ratio = _settings.TakeProfit.SellRatios[i] / 100m;
            var reason = $"分批止盈 TP{i + 1}({_settings.TakeProfit.TargetsPercent[i]}%)";
            return new PartialTakeProfitDecision(i, ratio, reason);
        }

        return null;
    }

    public bool ShouldTakeProfit(Position position, out decimal takeProfitPrice)
    {
        takeProfitPrice = position.EntryPriceUSD * (1 + _settings.Risk.TakeProfitPercent / 100m);
        return position.CurrentPriceUSD >= takeProfitPrice;
    }
}
