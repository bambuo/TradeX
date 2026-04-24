using Casbin;

namespace TradeX.Infrastructure.Casbin;

public class CasbinEnforcer
{
    private readonly Enforcer _enforcer;

    public CasbinEnforcer()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Casbin", "model.conf");
        var policyPath = Path.Combine(AppContext.BaseDirectory, "Casbin", "policy.csv");

        if (!File.Exists(modelPath))
            modelPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TradeX.Infrastructure", "Casbin", "model.conf");
        if (!File.Exists(policyPath))
            policyPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "TradeX.Infrastructure", "Casbin", "policy.csv");

        _enforcer = new Enforcer(modelPath, policyPath);
    }

    public bool Enforce(string role, string path, string method)
    {
        return _enforcer.Enforce(role, path, method);
    }
}
