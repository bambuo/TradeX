using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Migration;

namespace TradeX.Tests.Trading;

public class LegacyStrategyScannerTests
{
    private static LegacyStrategyScanner Build(IStrategyRepository repo) =>
        new(repo, Substitute.For<ILogger<LegacyStrategyScanner>>());

    [Fact]
    public async Task ScanAsync_StrategyWithRef_FlaggedAsLegacy()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "rel-cmp",
            EntryCondition = """{"operator":"","indicator":"SMA_50","comparison":">","value":1.02,"ref":"SMA_20"}""",
            ExitCondition = "{}"
        }]);

        var report = await Build(repo).ScanAsync();

        Assert.Single(report.LegacyStrategies);
        Assert.Contains(report.LegacyStrategies[0].Issues, i => i.Contains("ref"));
    }

    [Fact]
    public async Task ScanAsync_NestedConditions_RecursivelyScans()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "nested",
            EntryCondition = """{"operator":"AND","conditions":[{"operator":"","indicator":"SMA_50","comparison":">","value":1.02,"ref":"SMA_20"}]}""",
            ExitCondition = "{}"
        }]);

        var report = await Build(repo).ScanAsync();

        Assert.Single(report.LegacyStrategies);
        Assert.Contains(report.LegacyStrategies[0].Issues, i => i.Contains("ref") && i.Contains("[0]"));
    }

    [Fact]
    public async Task ScanAsync_CleanStrategy_NotFlagged()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "clean",
            EntryCondition = """{"operator":"","indicator":"RSI","comparison":"CA","value":30}""",
            ExitCondition = "{}"
        }]);

        var report = await Build(repo).ScanAsync();

        Assert.Empty(report.LegacyStrategies);
        Assert.Equal(1, report.TotalScanned);
    }

    [Fact]
    public async Task ScanAsync_MalformedJson_RecordedAsIssue()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "broken",
            EntryCondition = "not json{",
            ExitCondition = "{}"
        }]);

        var report = await Build(repo).ScanAsync();

        Assert.Single(report.LegacyStrategies);
        Assert.Contains(report.LegacyStrategies[0].Issues, i => i.Contains("JSON 解析失败"));
    }
}
