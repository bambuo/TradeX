namespace TradeX.Core.Models;

public class SystemConfig
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
