namespace TradeX.Core.Interfaces;

/// <summary>
/// 时间源抽象. 实盘走 SystemClock (DateTime.UtcNow), 回测走 BacktestClock (K 线时间驱动).
/// 引入此接口的目的是让策略评估代码在回测和实盘共享同一份实现, 而不被 DateTime.UtcNow 隐式耦合.
/// 推进策略: 渐进式替换 — 新代码必须用 IClock; 已有 DateTime.UtcNow 在重构相关模块时一并替换.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>测试 / 回测用; 通过 Advance 或直接赋值控制虚拟时间.</summary>
public sealed class FixedClock(DateTime initial) : IClock
{
    public DateTime UtcNow { get; set; } = initial;

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}
