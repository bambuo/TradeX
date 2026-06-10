using TradeX.Trading.Execution;

namespace TradeX.Tests.Trading;

public class KlineGapDetectorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Detect_NoGap_WhenLessThanOneInterval()
    {
        var d = new KlineGapDetector();
        var gaps = d.DetectGaps(T0, T0.AddSeconds(30), TimeSpan.FromMinutes(1));
        Assert.Empty(gaps);
    }

    [Fact]
    public void Detect_NoGap_WhenExactlyOneInterval()
    {
        // 1 个周期的延迟视为正常拉取节奏, 不算缺口
        var d = new KlineGapDetector();
        var gaps = d.DetectGaps(T0, T0.AddMinutes(1), TimeSpan.FromMinutes(1));
        Assert.Empty(gaps);
    }

    [Fact]
    public void Detect_OneGap_WhenLagExceedsTwoIntervals()
    {
        var d = new KlineGapDetector();
        var gaps = d.DetectGaps(T0, T0.AddMinutes(5), TimeSpan.FromMinutes(1));

        Assert.Single(gaps);
        Assert.Equal(T0.AddMinutes(1), gaps[0].StartAt);
        Assert.Equal(T0.AddMinutes(5), gaps[0].EndAt);
    }

    [Fact]
    public void Detect_LastSeenInFuture_ReturnsEmpty()
    {
        var d = new KlineGapDetector();
        var gaps = d.DetectGaps(T0.AddMinutes(5), T0, TimeSpan.FromMinutes(1));
        Assert.Empty(gaps);
    }

    [Fact]
    public void Detect_ZeroOrNegativeInterval_ReturnsEmpty()
    {
        var d = new KlineGapDetector();
        Assert.Empty(d.DetectGaps(T0, T0.AddMinutes(5), TimeSpan.Zero));
        Assert.Empty(d.DetectGaps(T0, T0.AddMinutes(5), TimeSpan.FromMinutes(-1)));
    }

    [Theory]
    [InlineData("1m", 60)]
    [InlineData("5m", 300)]
    [InlineData("15m", 900)]
    [InlineData("1h", 3600)]
    [InlineData("4h", 14400)]
    [InlineData("1d", 86400)]
    public void IntervalFromTimeframe_KnownValues_AreCorrect(string tf, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), KlineGapDetector.IntervalFromTimeframe(tf));
    }

    [Fact]
    public void IntervalFromTimeframe_Unknown_Throws()
    {
        Assert.Throws<ArgumentException>(() => KlineGapDetector.IntervalFromTimeframe("7m"));
    }
}
