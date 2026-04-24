using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController(IAuditLogRepository auditLogRepo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] DateTime? startUtc = null,
        [FromQuery] DateTime? endUtc = null,
        CancellationToken ct = default)
    {
        var (items, total) = await auditLogRepo.GetPagedAsync(page, pageSize,
            userId, action, resourceType, startUtc, endUtc, ct);

        return Ok(new
        {
            data = items.Select(a => new
            {
                a.Id, a.UserId, a.Action, a.Resource, a.ResourceId,
                a.Detail, a.IpAddress, a.Timestamp
            }),
            total
        });
    }
}
