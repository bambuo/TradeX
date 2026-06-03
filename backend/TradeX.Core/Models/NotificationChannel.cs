using TradeX.Core.Abstractions;
using TradeX.Core.Events;

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

public class NotificationChannel : AggregateRoot
{
    // EF Core 无参构造函数
    private NotificationChannel() { }

    public Guid Id { get; init; } = Guid.NewGuid();
    public NotificationChannelType Type { get; init; }
    public string Name { get; set; } = string.Empty;
    public string ConfigEncrypted { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public NotificationChannelStatus Status { get; private set; } = NotificationChannelStatus.Enabled;
    public DateTime? LastTestedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>工厂方法：创建通知渠道。</summary>
    public static NotificationChannel Create(NotificationChannelType type, string name, string configEncrypted)
    {
        return new NotificationChannel
        {
            Type = type,
            Name = name,
            ConfigEncrypted = configEncrypted
        };
    }

    /// <summary>启用通知渠道。</summary>
    public void Enable()
    {
        if (Status == NotificationChannelStatus.Enabled) return;
        var oldStatus = Status.ToString();
        Status = NotificationChannelStatus.Enabled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new NotificationChannelStatusChangedEvent(Id, Name, oldStatus, nameof(NotificationChannelStatus.Enabled)));
    }

    /// <summary>禁用通知渠道。</summary>
    public void Disable()
    {
        if (Status == NotificationChannelStatus.Disabled) return;
        var oldStatus = Status.ToString();
        Status = NotificationChannelStatus.Disabled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new NotificationChannelStatusChangedEvent(Id, Name, oldStatus, nameof(NotificationChannelStatus.Disabled)));
    }

    /// <summary>记录测试结果。</summary>
    public void RecordTestResult()
    {
        LastTestedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
