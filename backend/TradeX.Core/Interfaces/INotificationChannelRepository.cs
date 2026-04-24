using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface INotificationChannelRepository
{
    Task<NotificationChannel?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<NotificationChannel>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(NotificationChannel channel, CancellationToken ct = default);
    Task UpdateAsync(NotificationChannel channel, CancellationToken ct = default);
    Task DeleteAsync(NotificationChannel channel, CancellationToken ct = default);
}
