using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TradeX.Notifications;

public sealed class EmailSender(
    IOptions<EmailSettings> settings,
    ILogger<EmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        try
        {
            var s = settings.Value;
            using var client = new SmtpClient(s.SmtpHost, s.SmtpPort)
            {
                EnableSsl = s.UseSsl,
                Credentials = new NetworkCredential(s.Username, s.Password)
            };
            using var mail = new MailMessage
            {
                From = new MailAddress(s.FromAddress, s.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mail.To.Add(to);
            await client.SendMailAsync(mail, ct);
            logger.LogInformation("邮件已发送到 {To}", to);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "邮件发送失败 {To}", to);
        }
    }
}
