using Microsoft.Extensions.DependencyInjection;

namespace TradeX.Indicators;

public static class DependencyInjection
{
    public static IServiceCollection AddIndicators(this IServiceCollection services)
    {
        services.AddSingleton<IIndicatorService, IndicatorService>();
        services.AddSingleton<IIndicatorRegistry>(sp =>
        {
            var ind = sp.GetRequiredService<IIndicatorService>();
            var reg = new IndicatorRegistry();
            RegisterDefaults(reg, ind);
            return reg;
        });
        return services;
    }

    /// <summary>
    /// 默认 11 个指标. 复制自原 BacktestEngine 硬编码列表, 行为完全等价.
    /// 新增指标只需在此追加 reg.Register(...) 或在外部代码中调用 reg.Register(...).
    /// </summary>
    public static void RegisterDefaults(IIndicatorRegistry reg, IIndicatorService ind)
    {
        reg.Register("RSI", w => ind.CalculateRsi(w.Prices));
        reg.Register("SMA_20", w => ind.CalculateSma(w.Prices, 20));
        reg.Register("SMA_50", w => ind.CalculateSma(w.Prices, 50));
        reg.Register("EMA_20", w => ind.CalculateEma(w.Prices, 20));
        reg.Register("MACD_LINE", w => ind.CalculateMacd(w.Prices).MacdLine);
        reg.Register("MACD_SIGNAL", w => ind.CalculateMacd(w.Prices).SignalLine);
        reg.Register("BB_UPPER", w => ind.CalculateBollingerBands(w.Prices).UpperBand);
        reg.Register("BB_LOWER", w => ind.CalculateBollingerBands(w.Prices).LowerBand);
        reg.Register("OBV", w => ind.CalculateObv(w.Prices, w.Volumes));
        reg.Register("VOLUME_SMA", w => ind.CalculateVolumeSma(w.Volumes));
        reg.Register("RANGE_PCT", w => w.Open > 0 ? (w.High - w.Low) / w.Open * 100m : 0m);
    }
}
