using System.Collections.Concurrent;

namespace TradeX.Indicators;

public sealed class IndicatorRegistry : IIndicatorRegistry
{
    private readonly ConcurrentDictionary<string, IndicatorCompute> _computes = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> RegisteredNames => _computes.Keys.ToList().AsReadOnly();

    public void Register(string name, IndicatorCompute compute)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("指标名不能为空", nameof(name));
        _computes[name] = compute;
    }

    public Dictionary<string, decimal> ComputeAll(KlineWindow window)
    {
        var result = new Dictionary<string, decimal>(_computes.Count, StringComparer.Ordinal);
        foreach (var (name, compute) in _computes)
            result[name] = compute(window);
        return result;
    }

    public Dictionary<string, decimal> Compute(IEnumerable<string> names, KlineWindow window)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var name in names)
            if (_computes.TryGetValue(name, out var compute))
                result[name] = compute(window);
        return result;
    }
}
