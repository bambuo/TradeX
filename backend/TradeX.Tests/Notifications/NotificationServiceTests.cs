using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Notifications;
using TradeX.Notifications.Refit;

namespace TradeX.Tests.Notifications;

public class NotificationServiceTests
{
    private static NotificationService CreateService(
        ITelegramSender? telegram = null,
        IDiscordSender? discord = null,
        IEmailSender? email = null,
        ITelegramBotApi? telegramApi = null,
        INotificationChannelRepository? channelRepo = null,
        IEncryptionService? encryption = null)
    {
        return new NotificationService(
            telegram ?? Substitute.For<ITelegramSender>(),
            discord ?? Substitute.For<IDiscordSender>(),
            email ?? Substitute.For<IEmailSender>(),
            telegramApi ?? Substitute.For<ITelegramBotApi>(),
            channelRepo ?? Substitute.For<INotificationChannelRepository>(),
            encryption ?? Substitute.For<IEncryptionService>(),
            Substitute.For<ILogger<NotificationService>>());
    }

    [Fact]
    public async Task SendAsync_AllChannels_DoesNotThrow()
    {
        var service = CreateService();

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync(new NotificationEvent("order_filled", "TestStrategy",
                new Dictionary<string, object> { ["price"] = 50000m })));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_NullStrategyName_DoesNotThrow()
    {
        var service = CreateService();

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync(new NotificationEvent("test", null!,
                new Dictionary<string, object>())));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_WithAllEventTypes_DoesNotThrow()
    {
        var service = CreateService();

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
        var service = CreateService();

        var exception = await Record.ExceptionAsync(() =>
            service.SendAsync(new NotificationEvent("test", "S", new Dictionary<string, object>())));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendTestAsync_ChannelNotFound_ThrowsKeyNotFoundException()
    {
        var channelRepo = Substitute.For<INotificationChannelRepository>();
        channelRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((NotificationChannel?)null);

        var service = CreateService(channelRepo: channelRepo);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SendTestAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SendTestAsync_Telegram_SendsAndUpdatesLastTestedAt()
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Type = NotificationChannelType.Telegram,
            ConfigEncrypted = "encrypted-config",
            Status = NotificationChannelStatus.Enabled
        };

        var channelRepo = Substitute.For<INotificationChannelRepository>();
        channelRepo.GetByIdAsync(channel.Id, Arg.Any<CancellationToken>()).Returns(channel);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("encrypted-config").Returns(
            """{"botToken":"test-bot-token","chatId":"test-chat-id"}""");

        var telegramApi = Substitute.For<ITelegramBotApi>();
        telegramApi.SendMessageAsync(
                Arg.Any<string>(),
                Arg.Any<TelegramSendMessageRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new TelegramResponse(true, null));

        var service = CreateService(
            channelRepo: channelRepo,
            encryption: encryption,
            telegramApi: telegramApi);

        await service.SendTestAsync(channel.Id);

        await channelRepo.Received(1).UpdateAsync(Arg.Is<NotificationChannel>(c =>
            c.LastTestedAt.HasValue), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTestAsync_Telegram_MissingBotToken_Throws()
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Type = NotificationChannelType.Telegram,
            ConfigEncrypted = "encrypted-config"
        };

        var channelRepo = Substitute.For<INotificationChannelRepository>();
        channelRepo.GetByIdAsync(channel.Id, Arg.Any<CancellationToken>()).Returns(channel);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("encrypted-config").Returns(
            """{"chatId":"test-chat-id"}""");

        var service = CreateService(channelRepo: channelRepo, encryption: encryption);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestAsync(channel.Id));
    }

    [Fact]
    public async Task SendTestAsync_Discord_SendsAndUpdatesLastTestedAt()
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Type = NotificationChannelType.Discord,
            ConfigEncrypted = "encrypted-config"
        };

        var channelRepo = Substitute.For<INotificationChannelRepository>();
        channelRepo.GetByIdAsync(channel.Id, Arg.Any<CancellationToken>()).Returns(channel);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("encrypted-config").Returns(
            """{"webhookUrl":"https://discord.com/api/webhooks/test"}""");

        var discord = Substitute.For<IDiscordSender>();

        var service = CreateService(
            discord: discord,
            channelRepo: channelRepo,
            encryption: encryption);

        await service.SendTestAsync(channel.Id);

        await discord.Received(1).SendMessageAsync(
            "https://discord.com/api/webhooks/test",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTestAsync_Email_SendsAndUpdatesLastTestedAt()
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Type = NotificationChannelType.Email,
            ConfigEncrypted = "encrypted-config"
        };

        var channelRepo = Substitute.For<INotificationChannelRepository>();
        channelRepo.GetByIdAsync(channel.Id, Arg.Any<CancellationToken>()).Returns(channel);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("encrypted-config").Returns(
            """{"host":"smtp.test.com","port":"587","toAddress":"test@test.com"}""");

        var email = Substitute.For<IEmailSender>();

        var service = CreateService(
            email: email,
            channelRepo: channelRepo,
            encryption: encryption);

        await service.SendTestAsync(channel.Id);

        await email.Received(1).SendAsync(
            "test@test.com",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTestAsync_DisabledChannel_ThrowsInvalidOperationException()
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Type = NotificationChannelType.Telegram,
            Status = NotificationChannelStatus.Disabled,
            ConfigEncrypted = "encrypted-config"
        };

        var channelRepo = Substitute.For<INotificationChannelRepository>();
        channelRepo.GetByIdAsync(channel.Id, Arg.Any<CancellationToken>()).Returns(channel);

        var service = CreateService(channelRepo: channelRepo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestAsync(channel.Id));

        Assert.Contains("禁用", ex.Message);
    }
}
