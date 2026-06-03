using TradeX.Core.Abstractions;
using TradeX.Core.Enums;
using TradeX.Core.Events;

namespace TradeX.Core.Models;

public enum ExchangeStatus
{
    Enabled,
    Disabled
}

public class Exchange : AggregateRoot
{
    // EF Core 无参构造函数
    public Exchange() { }

    /// <summary>工厂方法：创建交易所配置。</summary>
    public static Exchange Create(Guid createdBy, string name, ExchangeType type,
        string apiKeyEncrypted, string secretKeyEncrypted, string? passphraseEncrypted = null,
        Guid? traderId = null)
    {
        return new Exchange
        {
            CreatedBy = createdBy,
            Name = name,
            Type = type,
            ApiKeyEncrypted = apiKeyEncrypted,
            SecretKeyEncrypted = secretKeyEncrypted,
            PassphraseEncrypted = passphraseEncrypted,
            TraderId = traderId
        };
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? TraderId { get; init; }
    public string Name { get; set; } = string.Empty;
    public ExchangeType Type { get; set; }
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public string SecretKeyEncrypted { get; set; } = string.Empty;
    public string? PassphraseEncrypted { get; set; }
    public ExchangeStatus Status { get; set; } = ExchangeStatus.Enabled;
    public DateTime? LastTestedAt { get; set; }
    public string? TestResult { get; set; }
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ─────────────── 领域方法 ───────────────

    /// <summary>启用交易所。</summary>
    public void Enable()
    {
        var old = Status.ToString();
        Status = ExchangeStatus.Enabled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new ExchangeConnectionChangedEvent(Id, TraderId, old, Status.ToString()));
    }

    /// <summary>禁用交易所。</summary>
    public void Disable()
    {
        var old = Status.ToString();
        Status = ExchangeStatus.Disabled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new ExchangeConnectionChangedEvent(Id, TraderId, old, Status.ToString()));
    }

    /// <summary>记录连接测试结果。</summary>
    public void RecordTestResult(bool success, string? message = null)
    {
        LastTestedAt = DateTime.UtcNow;
        TestResult = message ?? (success ? "OK" : "Failed");
        UpdatedAt = DateTime.UtcNow;
    }
}
