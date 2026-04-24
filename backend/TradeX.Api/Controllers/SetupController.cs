using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Enums;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data;
using static BCrypt.Net.BCrypt;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/setup")]
public class SetupController(TradeXDbContext db) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var hasSuperAdmin = await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin, ct);
        return Ok(new { isInitialized = hasSuperAdmin });
    }

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize([FromBody] InitializeRequest request, CancellationToken ct)
    {
        var hasSuperAdmin = await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin, ct);
        if (hasSuperAdmin)
            return Conflict(new { code = "SETUP_ALREADY_INITIALIZED", message = "系统已初始化" });

        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length < 3)
            return BadRequest(new { code = "SETUP_INVALID_INPUT", message = "用户名至少 3 个字符" });

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return BadRequest(new { code = "SETUP_INVALID_INPUT", message = "密码至少 8 个字符" });

        var jwtSecret = string.IsNullOrWhiteSpace(request.JwtSecret)
            ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            : request.JwtSecret;

        var superAdmin = new User
        {
            Username = request.UserName,
            Email = "superadmin@tradex.local",
            PasswordHash = HashPassword(request.Password),
            Role = UserRole.SuperAdmin,
            Status = UserStatus.PendingMfa,
            IsMfaEnabled = false,
            RecoveryCodesJson = "[]"
        };
        db.Users.Add(superAdmin);

        db.SystemConfigs.Add(new SystemConfig { Key = "jwt.secret", Value = jwtSecret });
        db.SystemConfigs.Add(new SystemConfig { Key = "jwt.access_token_expires_minutes", Value = "30" });
        db.SystemConfigs.Add(new SystemConfig { Key = "jwt.refresh_token_expires_days", Value = "7" });
        db.SystemConfigs.Add(new SystemConfig { Key = "risk.default_slippage_percent", Value = "0.1" });
        db.SystemConfigs.Add(new SystemConfig { Key = "risk.max_daily_loss_percent", Value = "10" });
        db.SystemConfigs.Add(new SystemConfig { Key = "risk.max_drawdown_percent", Value = "25" });
        db.SystemConfigs.Add(new SystemConfig { Key = "risk.cooldown_seconds", Value = "300" });
        db.SystemConfigs.Add(new SystemConfig { Key = "risk.consecutive_loss_limit", Value = "5" });
        db.SystemConfigs.Add(new SystemConfig { Key = "data.kline_warmup_days", Value = "3" });
        db.SystemConfigs.Add(new SystemConfig { Key = "data.kline_warmup_interval", Value = "15m" });

        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    public record InitializeRequest(string UserName, string Password, string? JwtSecret);
}
