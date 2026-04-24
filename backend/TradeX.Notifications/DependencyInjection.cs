using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;

namespace TradeX.Notifications;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddHttpClient<ITelegramSender, TelegramSender>();
        services.AddHttpClient<IDiscordSender, DiscordSender>();
        services.AddTransient<IEmailSender, EmailSender>();
        services.AddTransient<INotificationService, NotificationService>();
        services.Configure<TelegramSettings>(_ => { });
        services.Configure<DiscordSettings>(_ => { });
        services.Configure<EmailSettings>(_ => { });
        return services;
    }
}
