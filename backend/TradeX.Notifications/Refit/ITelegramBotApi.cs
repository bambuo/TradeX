using Refit;

namespace TradeX.Notifications.Refit;

public interface ITelegramBotApi
{
    [Post("/bot{botToken}/sendMessage")]
    Task<TelegramResponse> SendMessageAsync(string botToken, [Body] TelegramSendMessageRequest request, CancellationToken ct = default);
}

public record TelegramSendMessageRequest(
    string ChatId,
    string Text,
    string ParseMode = "Markdown");

public record TelegramResponse(
    bool Ok,
    TelegramMessageResult? Result);

public record TelegramMessageResult(
    long MessageId,
    TelegramChat Chat,
    string? Text);

public record TelegramChat(
    long Id,
    string? Title,
    string? Username);
