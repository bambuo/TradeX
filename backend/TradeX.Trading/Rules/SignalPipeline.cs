using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>
/// 信号管线：按依赖图拓扑排序执行信号生成器，维护 PrevSignals 缓存用于穿越判断（R1 一致性关键）。
/// 昂贵生成器走异步缓存路径（R7）。
/// </summary>
public sealed class SignalPipeline : ISignalPipeline
{
    private readonly ISignalGenerator[] _generators;
    private readonly ISignalGenerator[] _expensiveGenerators;
    private readonly List<string> _topoOrder;
    private readonly ExpensiveCache _expensiveCache = new();
    private readonly object _mu = new();
    private readonly ILogger<SignalPipeline> _logger;

    private Dictionary<string, Signal> _prevSignals = [];
    private SignalBus? _bus;

    public SignalPipeline(IEnumerable<ISignalGenerator> generators, ILogger<SignalPipeline> logger)
    {
        _logger = logger;
        var all = generators.ToList();

        var normal = new List<ISignalGenerator>();
        var expensive = new List<ISignalGenerator>();
        foreach (var gen in all)
        {
            if (gen is IExpensiveSignalGenerator eg && eg.IsExpensive)
                expensive.Add(gen);
            else
                normal.Add(gen);
        }

        // 构建依赖图并拓扑排序
        var graph = new DepGraph();
        var genMap = all.ToDictionary(g => g.Name);
        foreach (var gen in all)
            graph.AddGenerator(gen.Name, gen.Deps);

        var order = graph.TopologicalOrder();

        // 按拓扑序重排普通生成器
        var sortedNormal = new List<ISignalGenerator>();
        foreach (var name in order)
        {
            if (genMap.TryGetValue(name, out var gen))
            {
                if (gen is IExpensiveSignalGenerator eg2 && eg2.IsExpensive)
                    continue;
                sortedNormal.Add(gen);
            }
        }

        _generators = [.. sortedNormal];
        _expensiveGenerators = [.. expensive];
        _topoOrder = order;
    }

    /// <summary>注入信号总线，每次 Run 完成后发布信号。</summary>
    public void SetBus(SignalBus bus) => _bus = bus;

    /// <summary>校验生成器的依赖关系，返回所有错误。</summary>
    public List<DepGraph.DepError> ValidateDeps()
    {
        var graph = new DepGraph();
        foreach (var gen in _generators)
            graph.AddGenerator(gen.Name, gen.Deps);
        foreach (var gen in _expensiveGenerators)
            graph.AddGenerator(gen.Name, gen.Deps);
        return graph.Validate();
    }

    /// <summary>运行信号管线，返回所有生成的信号。</summary>
    public async Task<Dictionary<string, Signal>> RunAsync(SignalContext ctx, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return [];

        Dictionary<string, Signal> allSignals;
        lock (_mu)
        {
            allSignals = new Dictionary<string, Signal>(_generators.Length + _expensiveGenerators.Length);
            ctx.PrevSignals = _prevSignals;

            // 执行普通生成器（同步，按拓扑序）
            foreach (var gen in _generators)
            {
                if (ct.IsCancellationRequested) break;

                var sig = SafeGenerate(gen, ctx);
                if (sig is null)
                {
                    // S2: Generate 返回 null 时，将 prevSignals 中原值以 Stale 保留
                    if (_prevSignals.TryGetValue(gen.Name, out var prev))
                    {
                        var stale = new Signal
                        {
                            Name = prev.Name,
                            Value = prev.Value,
                            PrevValue = prev.PrevValue,
                            Quality = SignalQuality.Stale,
                            Meta = prev.Meta,
                        };
                        allSignals[gen.Name] = stale;
                        ctx.PrevSignals[gen.Name] = stale;
                    }
                    continue;
                }

                if (_prevSignals.TryGetValue(gen.Name, out var prevSig))
                    sig.PrevValue = prevSig.Value;

                allSignals[gen.Name] = sig;
                // S1: 将本轮已生成的信号合并到 PrevSignals，使后续生成器可发现
                ctx.PrevSignals[gen.Name] = sig;
            }
        }

        // 注入昂贵生成器缓存（标记 Stale）
        lock (_expensiveCache.Mu)
        {
            foreach (var (name, cached) in _expensiveCache.Results)
            {
                var stale = new Signal
                {
                    Name = cached.Name,
                    Value = cached.Value,
                    PrevValue = _prevSignals.TryGetValue(name, out var prev) ? prev.Value : cached.PrevValue,
                    Quality = SignalQuality.Stale,
                    Meta = cached.Meta,
                };
                allSignals[name] = stale;
            }
        }

        // 异步启动昂贵生成器计算
        if (_expensiveGenerators.Length > 0 && !_expensiveCache.Computing)
        {
            _ = Task.Run(() => ComputeExpensiveAsync(ctx, ct), ct);
        }

        // 更新 prevSignals 快照
        lock (_mu)
        {
            _prevSignals = new Dictionary<string, Signal>(allSignals);
        }

        // 发布到总线
        if (_bus is not null)
            await _bus.PublishAsync(ctx.Pair, allSignals, ct);

        return allSignals;
    }

    // ─── 内部方法 ───

    private Signal? SafeGenerate(ISignalGenerator gen, SignalContext ctx)
    {
        try
        {
            var dict = gen.GenerateAsync(ctx).GetAwaiter().GetResult();
            // 每个生成器返回 Dictionary，取第一个
            if (dict is null || dict.Count == 0) return null;
            return dict.Values.First();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "信号生成器 {GeneratorName} 异常，降级", gen.Name);
            return null;
        }
    }

    private void ComputeExpensiveAsync(SignalContext ctx, CancellationToken ct)
    {
        _expensiveCache.Computing = true;
        try
        {
            var results = new Dictionary<string, Signal>();
            foreach (var gen in _expensiveGenerators)
            {
                if (ct.IsCancellationRequested) break;
                var sig = SafeGenerate(gen, ctx);
                if (sig is not null)
                    results[gen.Name] = sig;
            }
            lock (_expensiveCache.Mu)
            {
                _expensiveCache.Results = results;
            }
        }
        finally
        {
            _expensiveCache.Computing = false;
        }
    }

    /// <summary>从缓存获取昂贵信号。</summary>
    public Signal? GetExpensive(string name)
    {
        lock (_expensiveCache.Mu)
        {
            return _expensiveCache.Results.TryGetValue(name, out var sig) ? sig : null;
        }
    }

    // ─── 内部类型 ───

    internal sealed class ExpensiveCache
    {
        public readonly object Mu = new();
        public Dictionary<string, Signal> Results = [];
        public volatile bool Computing;
    }
}
