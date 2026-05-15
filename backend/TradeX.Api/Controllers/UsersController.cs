using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Api.Filters;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(IUserRepository userRepo) : ControllerBase
{
    private static string Fmt(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? dt.ToString("yyyy-MM-dd HH:mm:ss")
            : DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss");

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var users = await userRepo.GetAllAsync(ct);
        var result = users.Select(u => new
        {
            u.Id,
            UserName = u.Username,
            u.Email,
            u.Role,
            u.Status,
            u.IsMfaEnabled,
            CreatedAt = Fmt(u.CreatedAt),
            LastLoginAt = u.LastLoginAt.HasValue ? Fmt(u.LastLoginAt.Value) : null
        });
        return Ok(result);
    }

    [HttpPut("{id:guid}/role")]
    [RequireMfa]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var user = await userRepo.GetByIdAsync(id, ct);
        if (user is null)
            return NotFound(new { message = "用户不存在" });

        if (user.Role == UserRole.SuperAdmin)
            return BadRequest(new { message = "不能修改 SuperAdmin 角色" });

        if (!Enum.TryParse<UserRole>(request.Role, true, out var newRole))
            return BadRequest(new { message = "无效角色" });

        user.Role = newRole;
        await userRepo.UpdateAsync(user, ct);

        return Ok(new { message = "角色已更新" });
    }

    public record UpdateRoleRequest(string Role);
}
