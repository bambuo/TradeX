namespace TradeX.Core.Models;

public enum NotificationChannelType
{
    Telegram,
    Discord,
    Email
}

public enum NotificationChannelStatus
{
    Enabled,
    Disabled
}

public class NotificationChannel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public NotificationChannelType Type { get; init; }
    public string Name { get; set; } = string.Empty;
    public string ConfigEncrypted { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public NotificationChannelStatus Status { get; set; } = NotificationChannelStatus.Enabled;
    public DateTime? LastTestedAtUtc { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
