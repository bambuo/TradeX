namespace TradeX.Core.Models;

public class RecoveryCode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Code { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
}
