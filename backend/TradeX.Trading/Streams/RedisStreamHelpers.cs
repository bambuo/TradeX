using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TradeX.Trading.Streams;

/// <summary>
/// Redis Stream 通用辅助方法 — 用于事件总线、命令通道、回测任务桥三处复用。
///
/// 设计要点：
/// - <b>持久化</b>：消息留在 Stream 中（受 MAXLEN 裁剪），订阅者重启不丢消息
/// - <b>Consumer Group</b>：每个消费方独立 group，每条消息只被组内一个 consumer 处理
/// - <b>显式 ACK</b>：处理成功才 <c>XACK</c>；失败留在 PEL，下次读取重新投递
/// - <b>稳定 consumer 名</b>：默认 <c>Environment.MachineName</c>，保证重启后 PEL 关联不变
/// </summary>
public static class RedisStreamHelpers
{
    /// <summary>所有 stream 共用的载荷字段名。</summary>
    public const string PayloadField = "data";

    /// <summary>Stream 最大条目数（近似裁剪），约保留最近 ~7 天事件。</summary>
    public const int DefaultMaxLength = 10_000;

    /// <summary>幂等地建立 consumer group，若已存在则忽略 BUSYGROUP 错误。</summary>
    public static async Task EnsureConsumerGroupAsync(
        IDatabase db, string streamKey, string group, ILogger? logger = null)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(streamKey, group, position: "0", createStream: true);
            logger?.LogInformation("Consumer group 已创建: stream={Stream} group={Group}", streamKey, group);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            // 已存在，忽略
        }
    }

    /// <summary>稳定的 consumer 名（默认每台机器/容器一个），保证重启后 PEL 关联保留。</summary>
    public static string DefaultConsumerName() => Environment.MachineName;

    /// <summary>追加一条消息到 stream，并按 MAXLEN 近似裁剪。</summary>
    public static Task<RedisValue> AddAsync(IDatabase db, string streamKey, string payload)
        => db.StreamAddAsync(
            streamKey,
            PayloadField, payload,
            maxLength: DefaultMaxLength,
            useApproximateMaxLength: true);
}
