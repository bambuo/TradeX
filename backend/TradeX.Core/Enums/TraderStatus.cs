using System.Text.Json.Serialization;

namespace TradeX.Core.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TraderStatus
{
    Active,
    Disabled,
    Deleted
}
