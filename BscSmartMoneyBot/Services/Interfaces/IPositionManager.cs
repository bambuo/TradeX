using BscSmartMoneyBot.Core.Models;

namespace BscSmartMoneyBot.Services.Interfaces;

public interface IPositionManager
{
    Task UpdatePositionPricesAsync(CancellationToken ct);
    Task ManagePositionsAsync(CancellationToken ct);
    Task<TimeSpan> GetAdjustedPollIntervalAsync(CancellationToken ct);
}
