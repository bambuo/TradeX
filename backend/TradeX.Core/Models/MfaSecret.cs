namespace TradeX.Core.Models;

public class MfaSecret
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string SecretKey { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
