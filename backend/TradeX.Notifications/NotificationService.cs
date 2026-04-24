using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;

namespace TradeX.Notifications;

public class NotificationService(
    ITelegramSender telegramSender,
    IDiscordSender discordSender,
    IEmailSender emailSender,
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
        var testEvent = new NotificationEvent("test", null, new()
        {
            ["message"] = "这是一条来自 TradeX 的测试消息",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });

        var message = FormatMessage(testEvent);
        await telegramSender.SendMessageAsync("test", message, ct);
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
