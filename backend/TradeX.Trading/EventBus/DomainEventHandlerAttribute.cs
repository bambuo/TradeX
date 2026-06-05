namespace TradeX.Trading.EventBus;

/// <summary>
/// 标记方法或类为指定领域事件类型的消费者。
/// 支持标注在类上（类中方法全部监听同一事件）或方法上（一个类处理多种事件）。
/// </summary>
/// <param name="eventType">要消费的领域事件 CLR 类型。</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class DomainEventHandlerAttribute(Type eventType) : Attribute
{
    public Type EventType { get; } = eventType;
}
