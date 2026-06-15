using System.Collections.Concurrent;
using System.Threading.Channels;
using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>
/// 信号总线，负责将信号广播给所有策略绑定。
///
/// 并发语义：
///   - Publish 执行异步非阻塞 fan-out：O(1) 时间返回，绝不阻塞
///   - Subscribe 返回一个带缓冲（buffer=1）的 channel
///   - 慢 consumer 自动丢旧保新：如果 subscriber 的 channel 已满，
///     清空旧信号写入最新信号，保证每个 subscriber 始终拿到最新一轮信号
///
/// 关键约束（R1）：丢旧保新只在"边沿状态由 SignalPipeline 拥有"时才安全。
/// 投递层（SignalBus）可丢；边沿状态层（Pipeline.PrevValue）不可丢。
/// </summary>
public sealed class SignalBus
{
    private readonly ConcurrentDictionary<string, SubscriberEntry> _subscribers = new();
    private ulong _nextId;

    internal sealed class SubscriberEntry
    {
        public required ulong Id { get; init; }
        public required Channel<Dictionary<string, Signal>> Channel { get; init; }
        public bool Closed { get; set; }
    }

    /// <summary>发布一轮信号快照（异步非阻塞 fan-out）。</summary>
    public ValueTask PublishAsync(string pair, Dictionary<string, Signal> signals, CancellationToken ct = default)
    {
        foreach (var (_, sub) in _subscribers)
        {
            if (ct.IsCancellationRequested) break;
            if (sub.Closed) continue;

            // 非阻塞投递：channel 满时丢旧保新
            if (sub.Channel.Writer.TryWrite(signals))
                continue;

            // channel 已满，清空旧值写入新值（丢旧保新）
            sub.Channel.Reader.TryRead(out _);
            sub.Channel.Writer.TryWrite(signals);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>订阅某个交易对的信号更新。返回的 channel buffer=1。</summary>
    public (ulong Id, ChannelReader<Dictionary<string, Signal>> Reader) Subscribe(string pair)
    {
        var id = Interlocked.Increment(ref _nextId);
        var channel = Channel.CreateBounded<Dictionary<string, Signal>>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _subscribers[pair] = new SubscriberEntry { Id = id, Channel = channel };
        return (id, channel.Reader);
    }

    /// <summary>取消订阅并关闭 channel。</summary>
    public void Unsubscribe(string pair, ulong id)
    {
        if (_subscribers.TryGetValue(pair, out var sub) && sub.Id == id)
        {
            sub.Closed = true;
            sub.Channel.Writer.TryComplete();
            _subscribers.TryRemove(pair, out _);
        }
    }
}
