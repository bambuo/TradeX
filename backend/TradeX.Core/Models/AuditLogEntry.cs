namespace TradeX.Core.Models;

public class AuditLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? UserId { get; init; }
    public string Action { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string? Detail { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
