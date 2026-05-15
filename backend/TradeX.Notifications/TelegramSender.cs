using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using TradeX.Notifications.Refit;

namespace TradeX.Notifications;

public sealed class TelegramSender(
    ITelegramBotApi api,
    IOptions<TelegramSettings> settings,
    ILogger<TelegramSender> logger) : ITelegramSender
{
    public async Task SendMessageAsync(string chatId, string message, CancellationToken ct = default)
    {
        try
        {
            var payload = new TelegramSendMessageRequest(chatId, message);
            var response = await api.SendMessageAsync(settings.Value.BotToken, payload, ct);
            if (response.Ok)
                logger.LogInformation("Telegram 消息已发送到 ChatId: {ChatId}", chatId);
            else
                logger.LogWarning("Telegram 发送返回失败 ChatId: {ChatId}", chatId);
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Telegram 消息发送失败 ChatId: {ChatId}", chatId);
        }
    }
}
