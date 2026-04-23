using BscSmartMoneyBot.Core.Models;

namespace BscSmartMoneyBot.Services.Interfaces;

public interface ISignalMonitor
{
    Task<IReadOnlyList<Signal>> FetchNewSignalsAsync(CancellationToken ct);
    Task<IReadOnlyList<Signal>> FilterSignalsAsync(IReadOnlyList<Signal> signals, CancellationToken ct);
    Task<IReadOnlyList<Signal>> SecurityScanAsync(IReadOnlyList<Signal> signals, CancellationToken ct);
}
