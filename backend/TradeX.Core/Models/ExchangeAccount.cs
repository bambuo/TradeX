using TradeX.Core.Enums;

namespace TradeX.Core.Models;

public enum ExchangeAccountStatus
{
    Enabled,
    Disabled
}

public class ExchangeAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? TraderId { get; init; }
    public string Name { get; set; } = string.Empty;
    public ExchangeType Type { get; set; }
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public string SecretKeyEncrypted { get; set; } = string.Empty;
    public string? PassphraseEncrypted { get; set; }
    public ExchangeAccountStatus Status { get; set; } = ExchangeAccountStatus.Enabled;
    public DateTime? LastTestedAtUtc { get; set; }
    public string? TestResult { get; set; }
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
