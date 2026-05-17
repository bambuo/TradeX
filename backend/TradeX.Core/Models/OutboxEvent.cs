namespace TradeX.Core.Models;

/// <summary>
/// Outbox 事件表 — 把消息发布与业务事务原子绑定。
/// 业务事务内：写业务表 + INSERT outbox_events 一并提交。
/// 后台 relay 服务轮询 Status=Pending 行，发布到消息中间件，成功后置 Sent。
/// </summary>
public class OutboxEvent
{
    public long Id { get; init; }
    /// <summary>事件类型标签（如 "OrderPlaced"），消费方据此路由。</summary>
    public string Type { get; init; } = string.Empty;
    /// <summary>消息载荷 JSON。结构由 Type 决定。</summary>
    public string PayloadJson { get; init; } = string.Empty;
    /// <summary>关联的 TraderId（可空），便于按用户聚合查询。</summary>
    public Guid? TraderId { get; init; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,    // 重试若干次仍失败，留待人工排查
}
