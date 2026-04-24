using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Notifications;

namespace TradeX.Tests.Notifications;

public class NotificationServiceTests
{
    [Fact]
    public async Task SendAsync_AllChannels_DoesNotThrow()
    {
        var telegram = Substitute.For<ITelegramSender>();
        var discord = Substitute.For<IDiscordSender>();
        var email = Substitute.For<IEmailSender>();

        var service = new NotificationService(
            telegram, discord, email,
            Substitute.For<ILogger<NotificationService>>());

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync(new NotificationEvent("order_filled", "TestStrategy",
                new Dictionary<string, object> { ["price"] = 50000m })));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_NullStrategyName_DoesNotThrow()
    {
        var service = new NotificationService(
            Substitute.For<ITelegramSender>(),
            Substitute.For<IDiscordSender>(),
            Substitute.For<IEmailSender>(),
            Substitute.For<ILogger<NotificationService>>());

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync(new NotificationEvent("test", null!,
                new Dictionary<string, object>())));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_WithAllEventTypes_DoesNotThrow()
    {
        var service = new NotificationService(
            Substitute.For<ITelegramSender>(),
            Substitute.For<IDiscordSender>(),
            Substitute.For<IEmailSender>(),
            Substitute.For<ILogger<NotificationService>>());

        var events = new[]
        {
            new NotificationEvent("order_filled", "S1", new Dictionary<string, object> { ["pnl"] = 100m }),
            new NotificationEvent("position_opened", "S2", new Dictionary<string, object> { ["quantity"] = 0.5m }),
            new NotificationEvent("risk_alert", null!, new Dictionary<string, object> { ["level"] = "Warning" })
        };

        foreach (var e in events)
        {
            var exception = await Record.ExceptionAsync(() => service.SendAsync(e));
            Assert.Null(exception);
        }
    }

    [Fact]
    public async Task SendAsync_EmptyData_DoesNotThrow()
    {
        var service = new NotificationService(
            Substitute.For<ITelegramSender>(),
            Substitute.For<IDiscordSender>(),
            Substitute.For<IEmailSender>(),
            Substitute.For<ILogger<NotificationService>>());

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync(new NotificationEvent("test", "S", new Dictionary<string, object>())));

        Assert.Null(exception);
    }
}
