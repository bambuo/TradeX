namespace TradeX.Trading.Risk;

/// <summary>
/// 进程级全局熔断开关. 激活后:
///   1) 所有新订单一律拒绝 (CircuitBreakerHandler 已经读 settings.CircuitBreakerActive, 此处提供运行时切换的能力)
///   2) 把所有 Active 的 StrategyBinding 改为 Disabled (软暂停)
///   3) 发布 KillSwitchActivatedPayload 事件通知所有通道
///   4) 写审计日志
/// 解除时撤回 1) 与 2) 的逆操作不自动恢复策略 — 需要运营人员逐个确认重新启用 (避免误触发后无差别恢复).
/// </summary>
public interface IKillSwitch
{
    bool IsActive { get; }
    string? LastReason { get; }
    DateTime? LastActivatedAtUtc { get; }

    Task ActivateAsync(string reason, Guid? actorUserId, CancellationToken ct = default);
    Task DeactivateAsync(string reason, Guid? actorUserId, CancellationToken ct = default);
}
