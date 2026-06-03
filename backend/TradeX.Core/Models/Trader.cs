using TradeX.Core.Abstractions;
using TradeX.Core.Enums;
using TradeX.Core.Events;

namespace TradeX.Core.Models;

public class Trader : AggregateRoot
{
    // EF Core 无参构造函数
    public Trader() { }

    /// <summary>工厂方法：创建交易员。</summary>
    public static Trader Create(Guid userId, string name, string? avatarColor = null, string? style = null)
    {
        return new Trader
        {
            UserId = userId,
            Name = name,
            AvatarColor = avatarColor,
            Style = style
        };
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Name { get; set; } = string.Empty;
    public TraderStatus Status { get; set; } = TraderStatus.Active;
    public string? AvatarColor { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Style { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // ─────────────── 领域方法 ───────────────

    /// <summary>激活交易员。</summary>
    public void Activate()
    {
        if (Status != TraderStatus.Disabled) return;
        var old = Status.ToString();
        Status = TraderStatus.Active;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new TraderStatusChangedEvent(Id, UserId, old, Status.ToString()));
    }

    /// <summary>禁用交易员。</summary>
    public void Disable()
    {
        if (Status == TraderStatus.Disabled) return;
        var old = Status.ToString();
        Status = TraderStatus.Disabled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new TraderStatusChangedEvent(Id, UserId, old, Status.ToString()));
    }

    /// <summary>重命名。</summary>
    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("名称不能为空", nameof(newName));
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>更新头像。</summary>
    public void UpdateAvatar(string avatarUrl)
    {
        AvatarUrl = avatarUrl;
        UpdatedAt = DateTime.UtcNow;
    }
}
