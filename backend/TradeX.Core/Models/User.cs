using TradeX.Core.Enums;

namespace TradeX.Core.Models;

public enum UserStatus
{
    PendingMfa,
    Active,
    Disabled
}

public class User
{
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
}
