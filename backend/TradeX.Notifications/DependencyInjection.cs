using Microsoft.Extensions.DependencyInjection;
using Refit;
using TradeX.Core.Interfaces;
using TradeX.Notifications.Refit;

namespace TradeX.Notifications;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddRefitClient<ITelegramBotApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.telegram.org"));

        services.AddHttpClient<IDiscordSender, DiscordSender>();
        services.AddTransient<ITelegramSender, TelegramSender>();
        services.AddTransient<IEmailSender, EmailSender>();
        services.AddTransient<INotificationService, NotificationService>();
        services.Configure<TelegramSettings>(_ => { });
        services.Configure<DiscordSettings>(_ => { });
        services.Configure<EmailSettings>(_ => { });
        return services;
    }
}
