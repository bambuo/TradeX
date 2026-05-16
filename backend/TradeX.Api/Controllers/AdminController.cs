using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Api.Filters;
using TradeX.Trading.Commands;

namespace TradeX.Api.Controllers;

/// <summary>
/// 管理员运维端点。所有写动作都标 <see cref="RequireMfaAttribute"/>，且通过命令总线异步派发给 Worker。
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
public sealed class AdminController(IWorkerCommandPublisher commandPublisher) : ControllerBase
{
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
}
