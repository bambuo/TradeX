using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Notifications.Refit;

namespace TradeX.Notifications;

public class NotificationService(
    ITelegramSender telegramSender,
    IDiscordSender discordSender,
    IEmailSender emailSender,
    ITelegramBotApi telegramApi,
    INotificationChannelRepository channelRepo,
    IEncryptionService encryption,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task SendAsync(NotificationEvent @event, CancellationToken ct = default)
    {
        try
        {
            var message = FormatMessage(@event);

            await Task.WhenAll(
                SendTelegramAsync(message, ct),
                SendDiscordAsync(message, ct),
                SendEmailAsync(@event, message, ct));

            logger.LogInformation("通知已发送: Type={Type}, Strategy={Strategy}",
                @event.Type, @event.StrategyName ?? "N/A");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "通知发送失败: Type={Type}", @event.Type);
        }
    }

    public async Task SendTestAsync(Guid channelId, CancellationToken ct = default)
    {
        var channel = await channelRepo.GetByIdAsync(channelId, ct);
        if (channel is null)
            throw new KeyNotFoundException($"通知渠道不存在: {channelId}");

        if (channel.Status == NotificationChannelStatus.Disabled)
            throw new InvalidOperationException("通知渠道已禁用，请先启用后再测试");

        var configJson = encryption.Decrypt(channel.ConfigEncrypted);
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
        if (config is null)
            throw new InvalidOperationException("通知渠道配置解析失败");

        var testEvent = new NotificationEvent("test", null, new()
        {
            ["message"] = "这是一条来自 TradeX 的测试消息",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });

        var message = FormatMessage(testEvent);

        switch (channel.Type)
        {
            case NotificationChannelType.Telegram:
                await SendTelegramTestAsync(config, message, ct);
                break;
            case NotificationChannelType.Discord:
                await discordSender.SendMessageAsync(config["webhookUrl"], message, ct);
                break;
            case NotificationChannelType.Email:
                await emailSender.SendAsync(config["toAddress"], "TradeX 测试消息", message, ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel.Type), $"不支持的通知渠道类型: {channel.Type}");
        }

        channel.LastTestedAt = DateTime.UtcNow;
        await channelRepo.UpdateAsync(channel, ct);
    }

    private async Task SendTelegramTestAsync(Dictionary<string, string> config, string message, CancellationToken ct)
    {
        config.TryGetValue("botToken", out var botToken);
        config.TryGetValue("chatId", out var chatId);

        if (string.IsNullOrEmpty(botToken))
            throw new InvalidOperationException("Telegram Bot Token 未配置");
        if (string.IsNullOrEmpty(chatId))
            throw new InvalidOperationException("Telegram Chat ID 未配置");

        var payload = new TelegramSendMessageRequest(chatId, message);
        var response = await telegramApi.SendMessageAsync(botToken, payload, ct);

        if (!response.Ok)
        {
            logger.LogError("Telegram 测试消息发送失败, BotToken={BotToken}, ChatId={ChatId}",
                botToken[..Math.Min(botToken.Length, 8)], chatId);
        }
    }

    private static string FormatMessage(NotificationEvent @event)
    {
        List<string> lines =
        [
            $"🔔 TradeX 通知",
            $"类型: {@event.Type}",
        ];

        if (!string.IsNullOrEmpty(@event.StrategyName))
            lines.Add($"策略: {@event.StrategyName}");

        foreach (var (key, value) in @event.Data)
            lines.Add($"{key}: {value}");

        return string.Join("\n", lines);
    }

    private Task SendTelegramAsync(string message, CancellationToken ct)
        => telegramSender.SendMessageAsync("default", message, ct);

    private Task SendDiscordAsync(string message, CancellationToken ct)
        => discordSender.SendMessageAsync("default", message, ct);

    private Task SendEmailAsync(NotificationEvent @event, string message, CancellationToken ct)
        => emailSender.SendAsync("admin@tradex.local", $"TradeX 通知: {@event.Type}", message, ct);
}
