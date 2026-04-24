using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TradeX.Notifications;

public sealed class TelegramSender(
    HttpClient httpClient,
    IOptions<TelegramSettings> settings,
    ILogger<TelegramSender> logger) : ITelegramSender
{
    private readonly string _baseUrl = $"https://api.telegram.org/bot{settings.Value.BotToken}";

    public async Task SendMessageAsync(string chatId, string message, CancellationToken ct = default)
    {
        try
        {
            var payload = new { chat_id = chatId, text = message, parse_mode = "Markdown" };
            var response = await httpClient.PostAsJsonAsync($"{_baseUrl}/sendMessage", payload, ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Telegram 消息已发送到 ChatId: {ChatId}", chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram 消息发送失败 ChatId: {ChatId}", chatId);
        }
    }
}
