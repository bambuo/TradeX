using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.AuditLogs;
using TradeX.Application.Common;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController(
    IUseCase<GetAuditLogsQuery, Result<List<AuditLogDto>>> getLogsUseCase,
    IUseCase<GetAuditLogsCountQuery, Result<int>> getCountUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? userId = null,
        [FromQuery] string? action = null,
        CancellationToken ct = default)
    {
        var query = new GetAuditLogsQuery(
            userId is not null ? Guid.Parse(userId) : null,
            action, page, pageSize);

        var itemsResult = await getLogsUseCase.ExecuteAsync(query, ct);

        var countQuery = new GetAuditLogsCountQuery(
            userId is not null ? Guid.Parse(userId) : null,
            action);
        var countResult = await getCountUseCase.ExecuteAsync(countQuery, ct);

        return Ok(new { data = itemsResult.Data, total = countResult.Data });
    }
}
