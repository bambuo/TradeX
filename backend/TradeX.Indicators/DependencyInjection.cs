using Microsoft.Extensions.DependencyInjection;

namespace TradeX.Indicators;

public static class DependencyInjection
{
    public static IServiceCollection AddIndicators(this IServiceCollection services)
    {
        services.AddSingleton<IIndicatorService, IndicatorService>();
        return services;
    }
}
