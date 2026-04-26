using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class NotificationChannelRepository(TradeXDbContext context) : INotificationChannelRepository
{
    public async Task<NotificationChannel?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.NotificationChannels.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<List<NotificationChannel>> GetAllAsync(CancellationToken ct = default)
        => await context.NotificationChannels.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(NotificationChannel channel, CancellationToken ct = default)
    {
        await context.NotificationChannels.AddAsync(channel, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(NotificationChannel channel, CancellationToken ct = default)
    {
        channel.UpdatedAt = DateTime.UtcNow;
        context.NotificationChannels.Update(channel);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(NotificationChannel channel, CancellationToken ct = default)
    {
        context.NotificationChannels.Remove(channel);
        await context.SaveChangesAsync(ct);
    }
}
