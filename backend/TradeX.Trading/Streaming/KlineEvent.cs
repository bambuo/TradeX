using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Trading.Streaming;

/// <summary>
/// 一条 K 线收盘事件。由 <see cref="KlineStreamManager"/> 在检测到新 K 线闭合时推送到
/// <c>Channel&lt;KlineEvent&gt;</c>，由 <see cref="StrategyEvaluationConsumer"/> 消费触发策略评估。
///
/// 与 <see cref="TradeEvent"/> 不同，KlineEvent 代表完整 K 线周期（OHLC + Volume）的关闭，
/// 用于基于技术指标的策略评估。产生频率取决于 <c>Interval</c>（如每分钟/每小时）。
/// </summary>
public readonly record struct KlineEvent(
    string Pair,
    ExchangeType ExchangeType,
    Guid ExchangeId,
    string Interval,
    Kline Kline);
