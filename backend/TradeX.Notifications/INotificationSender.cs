namespace TradeX.Notifications;

public interface ITelegramSender
{
    Task SendMessageAsync(string chatId, string message, CancellationToken ct = default);
}

public interface IDiscordSender
{
    Task SendMessageAsync(string webhookUrl, string message, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
