using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public static class DependencyInjection
{
    public static IServiceCollection AddExchange(this IServiceCollection services)
    {
        services.AddSingleton<IExchangeClientFactory, ExchangeClientFactory>();
        return services;
    }
}
