using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace TradeX.Notifications;

public sealed class DiscordSender(
    HttpClient httpClient,
    ILogger<DiscordSender> logger) : IDiscordSender
{
    public async Task SendMessageAsync(string webhookUrl, string message, CancellationToken ct = default)
    {
        try
        {
            var payload = new { content = message };
            var response = await httpClient.PostAsJsonAsync(webhookUrl, payload, ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Discord 消息已发送");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discord 消息发送失败");
        }
    }
}
