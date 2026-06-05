using TradeX.Core.Abstractions;
using TradeX.Core.Enums;
using TradeX.Core.Events;

namespace TradeX.Core.Models;

public enum UserStatus
{
    PendingMfa,
    Active,
    Disabled
}

public class User : AggregateRoot
{
    // EF Core 无参构造函数
    public User() { }

    /// <summary>工厂方法：创建新用户。</summary>
    public static User Create(string username, string email, string passwordHash, UserRole role = UserRole.Viewer)
    {
        return new User
        {
            Username = username,
            Email = email,
            PasswordHash = passwordHash,
            Role = role
        };
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Viewer;
    public UserStatus Status { get; set; } = UserStatus.PendingMfa;
    public bool IsMfaEnabled { get; set; }
    public string? MfaSecretEncrypted { get; set; }
    public string RecoveryCodesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsDeleted { get; set; }

    // ─────────────── 领域方法 ───────────────

    /// <summary>启用 MFA。</summary>
    public void EnableMfa(string secretEncrypted, string recoveryCodesJson)
    {
        if (IsMfaEnabled)
            throw new InvalidOperationException($"用户 {Id} 已启用 MFA");
        IsMfaEnabled = true;
        MfaSecretEncrypted = secretEncrypted;
        RecoveryCodesJson = recoveryCodesJson;
        if (Status == UserStatus.PendingMfa)
            Status = UserStatus.Active;
        AddDomainEvent(new MfaEnabledDomainEvent(Id));
    }

    /// <summary>禁用 MFA。</summary>
    public void DisableMfa()
    {
        IsMfaEnabled = false;
        MfaSecretEncrypted = null;
        RecoveryCodesJson = "[]";
    }

    /// <summary>记录登录。</summary>
    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        if (Status == UserStatus.PendingMfa && IsMfaEnabled)
            Status = UserStatus.Active;
        AddDomainEvent(new UserLoggedInDomainEvent(Id, LastLoginAt.Value));
    }

    /// <summary>变更角色。</summary>
    public void ChangeRole(UserRole newRole)
    {
        if (newRole == Role) return;
        Role = newRole;
    }

    /// <summary>禁用用户。</summary>
    public void Disable()
    {
        if (Status == UserStatus.Disabled) return;
        Status = UserStatus.Disabled;
    }
}
