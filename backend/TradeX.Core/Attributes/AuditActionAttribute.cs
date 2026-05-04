namespace TradeX.Core.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditActionAttribute(string method, string resource, string? label = null) : Attribute
{
    public string Method { get; } = method;
    public string Resource { get; } = resource;
    public string? Label { get; }
}
