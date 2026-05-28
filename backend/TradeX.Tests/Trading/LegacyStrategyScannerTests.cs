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
    public async Task ScanAsync_StrategyWithCA_FlaggedAsLegacy()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "legacy-cross",
            EntryCondition = """{"Operator":"","Indicator":"SMA_20","Comparison":"CA","Value":50000}""",
            ExitCondition = "{}"
        }]);

        var report = await Build(repo).ScanAsync();

        Assert.Single(report.LegacyStrategies);
        Assert.Contains(report.LegacyStrategies[0].Issues, i => i.Contains("CA"));
    }

    [Fact]
    public async Task ScanAsync_StrategyWithRef_FlaggedAsLegacy()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "rel-cmp",
            EntryCondition = """{"Operator":"","Indicator":"SMA_50","Comparison":">","Value":1.02,"Ref":"SMA_20"}""",
            ExitCondition = "{}"
        }]);

        var report = await Build(repo).ScanAsync();

        Assert.Single(report.LegacyStrategies);
        Assert.Contains(report.LegacyStrategies[0].Issues, i => i.Contains("Ref"));
    }

    [Fact]
    public async Task ScanAsync_NestedConditions_RecursivelyScans()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "nested",
            EntryCondition = """{"Operator":"AND","Conditions":[{"Operator":"","Indicator":"RSI","Comparison":"CB","Value":30}]}""",
            ExitCondition = "{}"
        }]);

        var report = await Build(repo).ScanAsync();

        Assert.Single(report.LegacyStrategies);
        Assert.Contains(report.LegacyStrategies[0].Issues, i => i.Contains("CB") && i.Contains("[0]"));
    }

    [Fact]
    public async Task ScanAsync_CleanStrategy_NotFlagged()
    {
        var repo = Substitute.For<IStrategyRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new Strategy
        {
            Id = Guid.NewGuid(), Name = "clean",
            EntryCondition = """{"Operator":"","Indicator":"RSI","Comparison":"CrossAbove","Value":30}""",
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
