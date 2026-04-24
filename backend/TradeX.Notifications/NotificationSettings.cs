namespace TradeX.Notifications;

public class TelegramSettings
{
    public string BotToken { get; set; } = string.Empty;
    public string DefaultChatId { get; set; } = string.Empty;
}

public class DiscordSettings
{
    public string DefaultWebhookUrl { get; set; } = string.Empty;
}

public class EmailSettings
{
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "TradeX";
}
