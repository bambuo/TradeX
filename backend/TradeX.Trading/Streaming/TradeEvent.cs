using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Trading.Streaming;

/// <summary>
/// 一条逐笔成交到达事件。由 <see cref="TradeStreamManager"/> 推送到 <c>Channel</c>,
/// 由 <see cref="StrategyEvaluationConsumer"/> 消费触发策略评估。
/// </summary>
public readonly record struct TradeEvent(
    string Pair,
    ExchangeType ExchangeType,
    Guid ExchangeId,
    Trade Trade);
