using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Migration;

/// <summary>
/// 扫描历史策略, 标记使用了"短代号"穿越运算符 (CA/CB) 或被删除字段 (Ref) 的条件树.
/// 这些值在 commit 9d57701 后仍可解析 (CA/CB 通过兼容别名生效, Ref 已恢复),
/// 但建议运营在 UI 上将其改为新格式以避免长期歧义.
/// </summary>
public sealed class LegacyStrategyScanner(IStrategyRepository strategyRepo, ILogger<LegacyStrategyScanner> logger)
{
    public async Task<LegacyScanReport> ScanAsync(CancellationToken ct = default)
    {
        var all = await strategyRepo.GetAllAsync(ct);
        var report = new LegacyScanReport(DateTime.UtcNow, all.Count, []);

        foreach (var s in all)
        {
            if (ct.IsCancellationRequested) break;
            var issues = new List<string>();
            CollectIssues(s.EntryCondition, "EntryCondition", issues);
            CollectIssues(s.ExitCondition, "ExitCondition", issues);
            if (issues.Count > 0)
                report.LegacyStrategies.Add(new LegacyStrategyEntry(s.Id, s.Name, issues));
        }

        if (report.LegacyStrategies.Count > 0)
            logger.LogWarning("历史策略扫描: 总计 {Total} 个, 含遗留语法 {Legacy} 个", report.TotalScanned, report.LegacyStrategies.Count);
        else
            logger.LogInformation("历史策略扫描: 总计 {Total} 个, 无遗留语法", report.TotalScanned);

        return report;
    }

    private static void CollectIssues(string? json, string field, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { issues.Add($"{field}: JSON 解析失败"); return; }
        using (doc)
            ScanNode(doc.RootElement, field, issues);
    }

    private static void ScanNode(JsonElement node, string path, List<string> issues)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        if (node.TryGetProperty("Comparison", out var cmp) && cmp.ValueKind == JsonValueKind.String)
        {
            var v = cmp.GetString();
            if (v == "CA" || v == "CB")
                issues.Add($"{path}: 使用短代号 '{v}', 建议改为 'CrossAbove'/'CrossBelow'");
        }
        if (node.TryGetProperty("Ref", out var refProp) && refProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(refProp.GetString()))
            issues.Add($"{path}: 使用 Ref='{refProp.GetString()}' 相对比较, 建议在 UI 上确认显式表达");

        if (node.TryGetProperty("Conditions", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var child in children.EnumerateArray())
                ScanNode(child, $"{path}.Conditions[{idx++}]", issues);
        }
    }
}

public sealed record LegacyScanReport(DateTime ScannedAtUtc, int TotalScanned, List<LegacyStrategyEntry> LegacyStrategies);

public sealed record LegacyStrategyEntry(Guid StrategyId, string Name, List<string> Issues);
