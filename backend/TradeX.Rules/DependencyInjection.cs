using Microsoft.Extensions.DependencyInjection;
using TradeX.Rules.Engine;

namespace TradeX.Rules;

public static class DependencyInjection
{
    public static IServiceCollection AddRulesEngine(this IServiceCollection services)
    {
        services.AddSingleton<ITriggerTracker, TriggerTracker>();
        services.AddScoped<IRuleEvaluator, RuleEvaluator>();
        return services;
    }
}
