using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TradeX.Api.Filters;
using TradeX.Trading.Commands;
using TradeX.Trading.Migration;
using TradeX.Trading.Risk;

namespace TradeX.Api.Controllers;

/// <summary>
/// 管理员运维端点。所有写动作都标 <see cref="RequireMfaAttribute"/>，且通过命令总线异步派发给 Worker。
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
public sealed class AdminController(
    IWorkerCommandPublisher commandPublisher,
    LegacyStrategyScanner legacyScanner,
    IKillSwitch killSwitch) : ControllerBase
{
    /// <summary>Kill Switch 当前状态.</summary>
    [HttpGet("kill-switch")]
    public IActionResult GetKillSwitch() => Ok(new
    {
        active = killSwitch.IsActive,
        reason = killSwitch.LastReason,
        activatedAtUtc = killSwitch.LastActivatedAtUtc
    });

    /// <summary>立即激活 Kill Switch: 暂停所有 Active StrategyBinding + Outbox 告警.</summary>
    [HttpPost("kill-switch/activate")]
    [RequireMfa]
    public async Task<IActionResult> ActivateKillSwitch([FromBody] KillSwitchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(new { error = "必须给出激活原因" });
        var userId = TryGetUserId();
        await killSwitch.ActivateAsync(req.Reason, userId, ct);
        return Ok(new { active = killSwitch.IsActive, reason = killSwitch.LastReason });
    }

    /// <summary>解除 Kill Switch. 策略 binding 不自动恢复, 需运营逐个启用.</summary>
    [HttpPost("kill-switch/deactivate")]
    [RequireMfa]
    public async Task<IActionResult> DeactivateKillSwitch([FromBody] KillSwitchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(new { error = "必须给出解除原因" });
        await killSwitch.DeactivateAsync(req.Reason, TryGetUserId(), ct);
        return Ok(new { active = killSwitch.IsActive });
    }

    private Guid? TryGetUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idStr, out var id) ? id : null;
    }

    public record KillSwitchRequest(string Reason);

    /// <summary>立即触发一次订单对账（手动覆盖 OrderReconciler 的周期巡检）。</summary>
    [HttpPost("reconcile-now")]
    [RequireMfa]
    public async Task<IActionResult> ReconcileNow(CancellationToken ct)
    {
        await commandPublisher.PublishAsync(WorkerCommandTypes.ReconcileNow, ct: ct);
        return Accepted(new
        {
            command = WorkerCommandTypes.ReconcileNow,
            message = "已派发到 Worker；执行结果请查看 Worker 日志或 Prometheus 指标"
        });
    }

    /// <summary>扫描所有策略, 列出仍使用旧 CA/CB 或 Ref 字段的条目, 引导运营人员清理.</summary>
    [HttpGet("legacy-strategies/scan")]
    public async Task<IActionResult> ScanLegacyStrategies(CancellationToken ct)
    {
        var report = await legacyScanner.ScanAsync(ct);
        return Ok(report);
    }
}
