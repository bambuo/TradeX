using Microsoft.Extensions.DependencyInjection;
using Refit;
using TradeX.Core.Interfaces;
using TradeX.Exchange.Handlers;
using TradeX.Exchange.Refit;

namespace TradeX.Exchange;

public static class DependencyInjection
{
    public static IServiceCollection AddExchange(this IServiceCollection services)
    {
        services.AddSingleton<IExchangeClientFactory, ExchangeClientFactory>();
        services.AddSingleton<IExchangeRateLimiter, TokenBucketRateLimiter>();
        return services;
    }

    public static IServiceCollection AddRefitExchangeClient<TInterface, THandler>(
        this IServiceCollection services,
        Uri baseAddress)
        where TInterface : class
        where THandler : DelegatingHandler
    {
        services.AddRefitClient<TInterface>()
            .ConfigureHttpClient(c => c.BaseAddress = baseAddress)
            .AddHttpMessageHandler<THandler>();

        return services;
    }
}
