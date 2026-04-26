using TradeX.Core.Enums;

namespace TradeX.Core.Models;

public class Trader
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Name { get; set; } = string.Empty;
    public TraderStatus Status { get; set; } = TraderStatus.Active;
    public string? AvatarColor { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Style { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
