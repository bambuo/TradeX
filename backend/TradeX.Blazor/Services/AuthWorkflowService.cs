using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using TradeX.Api.Services;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data;
using static BCrypt.Net.BCrypt;

namespace TradeX.Blazor.Services;

public sealed class AuthWorkflowService(
    TradeXDbContext db,
    IUserRepository userRepo,
    IRefreshTokenRepository refreshTokenRepo,
    JwtService jwtService,
    MfaService mfaService,
    IEncryptionService encryption)
{
    public async Task<SetupStatusResponse> GetSetupStatusAsync(CancellationToken ct = default)
    {
        var hasSuperAdmin = await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin, ct);
        return new SetupStatusResponse(hasSuperAdmin);
    }

    public async Task InitializeAsync(InitializeRequest request, CancellationToken ct = default)
    {
        var hasSuperAdmin = await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin, ct);
        if (hasSuperAdmin)
        {
            throw new InvalidOperationException("系统已初始化");
        }

        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length < 3)
        {
            throw new InvalidOperationException("用户名至少 3 个字符");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw new InvalidOperationException("密码至少 8 个字符");
        }

        var jwtSecret = string.IsNullOrWhiteSpace(request.JwtSecret)
            ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            : request.JwtSecret;

        db.Users.Add(new User
        {
            Username = request.UserName,
            Email = "superadmin@tradex.local",
            PasswordHash = HashPassword(request.Password),
            Role = UserRole.SuperAdmin,
            Status = UserStatus.PendingMfa,
            IsMfaEnabled = false,
            RecoveryCodesJson = "[]"
        });

        (string Key, string Value)[] defaultConfigs =
        [
            ("jwt.secret", jwtSecret),
            ("jwt.access_token_expires_minutes", "30"),
            ("jwt.refresh_token_expires_days", "7"),
            ("risk.default_slippage_percent", "0.1"),
            ("risk.max_daily_loss_percent", "10"),
            ("risk.max_drawdown_percent", "25"),
            ("risk.cooldown_seconds", "300"),
            ("risk.volatility_grid_dedup_seconds", "60"),
            ("risk.consecutive_loss_limit", "5"),
            ("data.kline_warmup_days", "3"),
            ("data.kline_warmup_interval", "15m")
        ];

        foreach (var (key, value) in defaultConfigs)
        {
            await AddSystemConfigIfMissingAsync(key, value, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task AddSystemConfigIfMissingAsync(string key, string value, CancellationToken ct)
    {
        var exists = await db.SystemConfigs.AnyAsync(x => x.Key == key, ct);
        if (!exists)
        {
            db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
        }
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userRepo.GetByUsernameAsync(request.Username, ct);
        if (user is null || !Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("用户名或密码错误");
        }

        if (user.Status == UserStatus.Disabled)
        {
            throw new UnauthorizedAccessException("用户已被禁用");
        }

        var mfaToken = jwtService.GenerateMfaToken(user);
        if (!user.IsMfaEnabled && user.Status == UserStatus.PendingMfa)
        {
            return new LoginResponse(false, mfaToken, true, 300);
        }

        return new LoginResponse(true, mfaToken, false, 300);
    }

    public async Task<MfaSetupResponse> SetupMfaAsync(string mfaToken, CancellationToken ct = default)
    {
        var user = await GetUserFromMfaTokenAsync(mfaToken, ct);
        var secret = mfaService.GenerateSecret(user.Id);
        user.MfaSecretEncrypted = encryption.Encrypt(secret.SecretKey);

        var recoveryCodes = mfaService.GenerateRecoveryCodes(user.Id);
        user.RecoveryCodesJson = JsonSerializer.Serialize(recoveryCodes.Select(c => c.Code).ToList());

        await userRepo.UpdateAsync(user, ct);

        var otpauthUrl = $"otpauth://totp/TradeX:{user.Username}?secret={secret.SecretKey}&issuer=TradeX";
        using var qr = new QRCodeGenerator();
        var qrData = qr.CreateQrCode(otpauthUrl, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(qrData);
        var qrBase64 = Convert.ToBase64String(png.GetGraphic(4));

        return new MfaSetupResponse(
            secret.SecretKey,
            otpauthUrl,
            $"data:image/png;base64,{qrBase64}",
            recoveryCodes.Select(c => c.Code).ToArray());
    }

    public async Task<AuthTokens> VerifyMfaSetupAsync(string mfaToken, string code, CancellationToken ct = default)
    {
        var user = await GetUserFromMfaTokenAsync(mfaToken, ct);
        if (string.IsNullOrWhiteSpace(user.MfaSecretEncrypted))
        {
            throw new InvalidOperationException("请先生成 MFA 密钥");
        }

        var secret = encryption.Decrypt(user.MfaSecretEncrypted);
        if (!mfaService.ValidateTotp(secret, code))
        {
            throw new UnauthorizedAccessException("MFA 验证码错误");
        }

        user.IsMfaEnabled = true;
        user.Status = UserStatus.Active;
        await userRepo.UpdateAsync(user, ct);

        return await CreateTokensAsync(user, ct);
    }

    public async Task<AuthTokens> VerifyMfaAsync(string mfaToken, string? totpCode, string? recoveryCode, CancellationToken ct = default)
    {
        var user = await GetUserFromMfaTokenAsync(mfaToken, ct);
        if (user.Status == UserStatus.Disabled)
        {
            throw new UnauthorizedAccessException("用户已被禁用");
        }

        if (!string.IsNullOrWhiteSpace(totpCode))
        {
            if (string.IsNullOrWhiteSpace(user.MfaSecretEncrypted))
            {
                throw new InvalidOperationException("MFA 未配置");
            }

            var secret = encryption.Decrypt(user.MfaSecretEncrypted);
            if (!mfaService.ValidateTotp(secret, totpCode))
            {
                throw new UnauthorizedAccessException("MFA 验证码错误");
            }
        }
        else if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var recoveryCodes = JsonSerializer.Deserialize<List<string>>(user.RecoveryCodesJson) ?? [];
            var normalizedCode = recoveryCode.Trim().ToUpperInvariant();
            var matchedIndex = recoveryCodes.FindIndex(c => c.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
            if (matchedIndex < 0)
            {
                throw new UnauthorizedAccessException("恢复码无效或已使用");
            }

            recoveryCodes.RemoveAt(matchedIndex);
            user.RecoveryCodesJson = JsonSerializer.Serialize(recoveryCodes);
        }
        else
        {
            throw new InvalidOperationException("请提供 TOTP 码或恢复码");
        }

        user.LastLoginAt = DateTime.UtcNow;
        if (user.Status == UserStatus.PendingMfa)
        {
            user.Status = UserStatus.Active;
        }

        await userRepo.UpdateAsync(user, ct);
        return await CreateTokensAsync(user, ct);
    }

    private async Task<User> GetUserFromMfaTokenAsync(string mfaToken, CancellationToken ct)
    {
        var principal = jwtService.ValidateMfaToken(mfaToken);
        if (principal is null)
        {
            throw new UnauthorizedAccessException("MFA Token 无效或已过期");
        }

        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await userRepo.GetByIdAsync(userId, ct);
        return user ?? throw new UnauthorizedAccessException("用户不存在");
    }

    private async Task<AuthTokens> CreateTokensAsync(User user, CancellationToken ct)
    {
        var accessToken = jwtService.GenerateAccessToken(user);
        var refreshTokenStr = jwtService.GenerateRefreshToken();

        await refreshTokenRepo.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(jwtService.RefreshTokenExpirationDays)
        }, ct);

        return new AuthTokens(accessToken, refreshTokenStr, user.Id, user.Username, user.Role.ToString());
    }
}

public sealed record SetupStatusResponse(bool IsInitialized);
public sealed record InitializeRequest(string UserName, string Password, string? JwtSecret = null);
public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(bool MfaRequired, string? MfaToken, bool MfaSetupRequired, int? ExpiresIn);
public sealed record MfaSetupResponse(string SecretKey, string QrCodeUrl, string QrCodeImage, IReadOnlyList<string> RecoveryCodes);
