using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Enums;
using TradeX.Core.ErrorCodes;
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
            return this.Conflict(BusinessErrorCode.SetupAlreadyInitialized, "系统已初始化");

        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length < 3)
            return this.BadRequest(BusinessErrorCode.SetupInvalidInput, "用户名至少 3 个字符");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return this.BadRequest(BusinessErrorCode.SetupInvalidInput, "密码至少 8 个字符");

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

        var existingKeys = await db.SystemConfigs.Select(s => s.Key).ToListAsync(ct);
        var defaults = new Dictionary<string, string>
        {
            ["jwt.secret"] = jwtSecret,
            ["jwt.access_token_expires_minutes"] = "30",
            ["jwt.refresh_token_expires_days"] = "7",
            ["risk.default_slippage_percent"] = "0.1",
            ["risk.max_daily_loss_percent"] = "10",
            ["risk.max_drawdown_percent"] = "25",
            ["risk.cooldown_seconds"] = "300",
            ["risk.volatility_grid_dedup_seconds"] = "60",
            ["risk.consecutive_loss_limit"] = "5",
            ["data.kline_warmup_days"] = "3",
            ["data.kline_warmup_interval"] = "15m"
        };

        foreach (var (key, value) in defaults)
        {
            if (!existingKeys.Contains(key))
                db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
        }

        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    public record InitializeRequest(string UserName, string Password, string? JwtSecret);
}
