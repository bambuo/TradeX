using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // 默认无指标; Worker/Api Program 可覆盖注入真实实现 (转发到 TradeXMetrics.NotificationsFailed)
        services.TryAddSingleton<INotificationMetrics, NullNotificationMetrics>();
        services.AddScoped<NotificationRetryPolicy>();
        services.Configure<TelegramSettings>(_ => { });
        services.Configure<DiscordSettings>(_ => { });
        services.Configure<EmailSettings>(_ => { });
        return services;
    }
}
