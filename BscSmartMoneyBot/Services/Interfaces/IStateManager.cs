using BscSmartMoneyBot.Core.Models;

namespace BscSmartMoneyBot.Services.Interfaces;

public interface IStateManager
{
    Task<BotState> LoadStateAsync(CancellationToken ct);
    Task SaveStateAsync(BotState state, CancellationToken ct);
    Task BackupStateAsync(CancellationToken ct);
}
