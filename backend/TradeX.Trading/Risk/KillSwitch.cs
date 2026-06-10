using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Trading.EventBus;
using TradeX.Trading.Events;
using TradeX.Trading.Observability;

namespace TradeX.Trading.Risk;

/// <summary>
/// 进程内 singleton 实现. 状态可被 CircuitBreakerHandler 读到 → 全部交易请求被拒.
/// 通过 IDomainEventBus 发布 KillSwitch 事件 (跨进程通知); 同进程内通过 IsActive 字段实时生效.
/// 暂未持久化到 DB — 进程重启后状态丢失. 这是有意为之: Kill Switch 是"应急"工具,
/// 重启过程已经是审计窗口, 启动后需要人工再次确认是否激活.
/// </summary>
public sealed class KillSwitch(IServiceScopeFactory scopeFactory, TradeXMetrics metrics, ILogger<KillSwitch> logger) : IKillSwitch
{
    private readonly object _lock = new();
    private volatile bool _active;
    private volatile string? _lastReason;
    private DateTime? _lastActivatedAt;

    public bool IsActive => _active;
    public string? LastReason => _lastReason;
    public DateTime? LastActivatedAt => _lastActivatedAt;

    public async Task ActivateAsync(string reason, Guid? actorUserId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_active) { logger.LogInformation("Kill Switch 已激活, 跳过重复激活"); return; }
            _active = true;
            _lastReason = reason;
            _lastActivatedAt = DateTime.UtcNow;
        }

        using var scope = scopeFactory.CreateScope();
        var bindingRepo = scope.ServiceProvider.GetRequiredService<IStrategyBindingRepository>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IDomainEventBus>();

        var actives = await bindingRepo.GetAllActiveAsync(ct);
        foreach (var binding in actives)
            binding.Status = BindingStatus.Disabled;
        await bindingRepo.UpdateRangeAsync(actives, ct);

        var payload = new KillSwitchActivatedPayload(reason, actorUserId, LastActivatedAt!.Value, actives.Count);
        await eventBus.PublishAsync(payload, ct);

        metrics.SetKillSwitchActive(true);
        metrics.KillSwitchActivations.Add(1, new KeyValuePair<string, object?>("reason", reason));
        logger.LogCritical("Kill Switch 已激活: Reason={Reason}, Actor={Actor}, DisabledBindings={Count}",
            reason, actorUserId, actives.Count);
    }

    public async Task DeactivateAsync(string reason, Guid? actorUserId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_active) { logger.LogInformation("Kill Switch 未激活, 跳过解除"); return; }
            _active = false;
            _lastReason = $"已解除: {reason}";
        }
        metrics.SetKillSwitchActive(false);
        logger.LogWarning("Kill Switch 已解除: Reason={Reason}, Actor={Actor}. 策略 binding 不自动恢复, 需运营逐个启用",
            reason, actorUserId);

        using var scope = scopeFactory.CreateScope();
        var eventBus = scope.ServiceProvider.GetRequiredService<IDomainEventBus>();
        var payload = new KillSwitchDeactivatedPayload(reason, actorUserId, DateTime.UtcNow);
        await eventBus.PublishAsync(payload, ct);
    }
}
