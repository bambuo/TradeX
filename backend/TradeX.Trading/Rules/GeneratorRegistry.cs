namespace TradeX.Trading.Rules;

/// <summary>
/// 信号生成器注册表。管理所有可用的信号生成器，提供注册、查找、列举功能。线程安全。
/// </summary>
public sealed class GeneratorRegistry
{
    private readonly Dictionary<string, ISignalGenerator> _generators = [];
    private readonly object _mu = new();

    /// <summary>注册一个信号生成器。如果名称已存在抛出 InvalidOperationException。</summary>
    public void Register(ISignalGenerator gen)
    {
        lock (_mu)
        {
            if (_generators.ContainsKey(gen.Name))
                throw new InvalidOperationException($"Signal generator '{gen.Name}' already registered");
            _generators[gen.Name] = gen;
        }
    }

    /// <summary>查找已注册的生成器。</summary>
    public ISignalGenerator? Lookup(string name)
    {
        lock (_mu)
        {
            return _generators.GetValueOrDefault(name);
        }
    }

    /// <summary>检查指定名称是否已注册。</summary>
    public bool Has(string name)
    {
        lock (_mu)
        {
            return _generators.ContainsKey(name);
        }
    }

    /// <summary>返回所有已注册生成器的名称列表。</summary>
    public List<string> ListNames()
    {
        lock (_mu)
        {
            return [.. _generators.Keys];
        }
    }

    /// <summary>返回所有已注册的生成器。</summary>
    public List<ISignalGenerator> ListAll()
    {
        lock (_mu)
        {
            return [.. _generators.Values];
        }
    }

    /// <summary>创建包含所有内置生成器的注册表。</summary>
    public static GeneratorRegistry CreateDefault()
    {
        var r = new GeneratorRegistry();

        // 技术指标
        r.Register(new Generators.SMAGenerator(20));
        r.Register(new Generators.SMAGenerator(50));
        r.Register(new Generators.SMAGenerator(200));
        r.Register(new Generators.EMAGenerator(12));
        r.Register(new Generators.EMAGenerator(26));
        r.Register(new Generators.RSIGenerator(14));
        r.Register(new Generators.MACDGenerator(12, 26, 9));
        r.Register(new Generators.MACDSignalGenerator());
        r.Register(new Generators.MACDHistogramGenerator());
        r.Register(new Generators.BollingerGenerator(20, 2.0));
        r.Register(new Generators.BollingerUpperGenerator(20, 2.0));
        r.Register(new Generators.BollingerLowerGenerator(20, 2.0));
        r.Register(new Generators.ATRGenerator(14));
        r.Register(new Generators.ADXGenerator(14));
        r.Register(new Generators.StochasticGenerator(14, 3));
        r.Register(new Generators.StochKGenerator());
        r.Register(new Generators.StochDGenerator());

        // 上下文指标
        r.Register(new Generators.DeviationFromAvgGenerator());
        r.Register(new Generators.PyramidingLevelGenerator());
        r.Register(new Generators.PositionNotionalGenerator());
        r.Register(new Generators.PositionPnlPctGenerator());
        r.Register(new Generators.PositionCountGenerator());
        r.Register(new Generators.PortfolioDrawdownGenerator());
        r.Register(new Generators.AvailableCashGenerator());

        // 市场信号
        r.Register(new Generators.FundingRateGenerator());
        r.Register(new Generators.Volatility24hGenerator(24));
        r.Register(new Generators.VolumeSpikeGenerator(20));
        r.Register(new Generators.BidAskRatioGenerator());
        r.Register(new Generators.LiquidityDepthGenerator(2.0));

        // 体制检测
        r.Register(new Generators.RegimeDetectorGenerator(5.0, 1.5, 25.0, 10.0));

        // 系统信号
        r.Register(new Generators.SignalQualityEstimatorGenerator(500));

        return r;
    }
}
