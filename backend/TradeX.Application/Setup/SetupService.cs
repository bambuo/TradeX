using System.Security.Cryptography;
using System.Text;
using TradeX.Application.Common;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Application.Setup;

/// <summary>
/// 系统初始化服务。封装首次设置逻辑（创建超级管理员 + 写入默认配置）。
/// </summary>
public sealed class SetupService(
    IUserRepository userRepo,
    ISystemConfigRepository configRepo) : ISetupService
{
    public async Task<bool> GetStatusAsync(CancellationToken ct = default)
    {
        var users = await userRepo.GetAllAsync(ct);
        return users.Any(u => u.Role == UserRole.SuperAdmin);
    }

    public async Task<Result> InitializeAsync(
        string userName, string password, string? jwtSecret, CancellationToken ct = default)
    {
        var users = await userRepo.GetAllAsync(ct);
        if (users.Any(u => u.Role == UserRole.SuperAdmin))
            return Result.Conflict("系统已初始化");

        if (string.IsNullOrWhiteSpace(userName) || userName.Length < 3)
            return Result.BadRequest("用户名至少 3 个字符");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return Result.BadRequest("密码至少 8 个字符");

        var jwtSecretStr = string.IsNullOrWhiteSpace(jwtSecret)
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            : jwtSecret;

        var superAdmin = User.Create(userName, "superadmin@tradex.local", HashPassword(password), UserRole.SuperAdmin);
        superAdmin.Status = UserStatus.PendingMfa;
        await userRepo.AddAsync(superAdmin, ct);

        var defaults = new Dictionary<string, string>
        {
            ["jwt.secret"] = jwtSecretStr,
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
            await configRepo.UpsertAsync(key, value, ct);

        return Result.NoContent();
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100000, HashAlgorithmName.SHA256, 32);
        var result = new byte[48];
        Buffer.BlockCopy(salt, 0, result, 0, 16);
        Buffer.BlockCopy(hash, 0, result, 16, 32);
        return Convert.ToBase64String(result);
    }
}
