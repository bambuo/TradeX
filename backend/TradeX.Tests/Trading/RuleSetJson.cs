using System.Globalization;

namespace TradeX.Tests.Trading;

/// <summary>
/// 测试用执行规则集 JSON 构造助手。把旧的 EntryCondition/ExitCondition 双条件
/// 改写为统一规则集：入场（noPosition）+ 全平（hasPosition）两条规则。
/// </summary>
internal static class RuleSetJson
{
    public const string True = """{"operator":"TRUE"}""";

    /// <summary>叶子条件 JSON 片段。</summary>
    public static string Leaf(string indicator, string comparison, decimal value, string? @ref = null)
    {
        var v = value.ToString(CultureInfo.InvariantCulture);
        return @ref is null
            ? $$"""{"indicator":"{{indicator}}","comparison":"{{comparison}}","value":{{v}}}"""
            : $$"""{"indicator":"{{indicator}}","comparison":"{{comparison}}","value":{{v}},"ref":"{{@ref}}"}""";
    }

    /// <summary>入场(noPosition, buy) + 全平(hasPosition, sellAll) 双规则集；when 片段直接嵌入。</summary>
    public static string EntryExit(string entryWhen, string exitWhen, string? entryThen = null) =>
        "{\"code\":\"t\",\"name\":\"t\",\"rules\":[" +
        "{\"code\":\"entry\",\"name\":\"e\",\"context\":\"noPosition\",\"priority\":1,\"when\":" + entryWhen +
            ",\"then\":" + (entryThen ?? "{\"action\":\"buy\"}") + "}," +
        "{\"code\":\"exit\",\"name\":\"x\",\"context\":\"hasPosition\",\"priority\":1,\"when\":" + exitWhen +
            ",\"then\":{\"action\":\"sellAll\"}}" +
        "]}";

    /// <summary>仅入场规则（noPosition）。</summary>
    public static string EntryOnly(string entryWhen, string then) =>
        "{\"code\":\"t\",\"name\":\"t\",\"rules\":[" +
        "{\"code\":\"entry\",\"name\":\"e\",\"context\":\"noPosition\",\"priority\":1,\"when\":" + entryWhen +
            ",\"then\":" + then + "}]}";
}
