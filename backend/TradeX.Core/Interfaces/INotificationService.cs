namespace TradeX.Core.Interfaces;

public record NotificationEvent(
    string Type,
    string? StrategyName,
    Dictionary<string, object> Data);

public interface INotificationService
{
    Task SendAsync(NotificationEvent @event, CancellationToken ct = default);
    Task SendTestAsync(Guid channelId, CancellationToken ct = default);
}
