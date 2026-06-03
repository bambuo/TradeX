using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;
using TradeX.Api.Services;
using TradeX.Application.Auth;
using TradeX.Application.Common;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IUserRepository userRepo,
    IRefreshTokenRepository refreshTokenRepo,
    IEncryptionService encryption,
    JwtService jwtService,
    MfaService mfaService,
    IUseCase<LoginCommand, Result<AuthResultDto>> loginUseCase,
    IUseCase<RefreshTokenCommand, Result<AuthResultDto>> refreshTokenUseCase,
    IUseCase<GetCurrentUserQuery, Result<AuthResultDto>> getCurrentUserUseCase) : ControllerBase
{
    public record LoginRequest(string Username, string Password);
    public record VerifyMfaRequest(string MfaToken, string? TotpCode, string? RecoveryCode);
    public record RefreshRequest(string RefreshToken);
    public record MfaSetupResponse(string SecretKey, string QrCodeUrl, string QrCodeImage, List<string> RecoveryCodes);
    public record MfaVerifyRequest(string Code);
    public record SendRecoveryCodesRequest(Guid UserId);

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await loginUseCase.ExecuteAsync(new LoginCommand(request.Username, request.Password), ct);
        if (!result.Success)
            return Unauthorized(new { message = result.Error });

        var authResult = result.Data!;
        var user = await userRepo.GetByUsernameAsync(request.Username, ct);

        if (user is null)
            return this.Unauthorized("用户名或密码错误");

        if (!user.IsMfaEnabled && user.Status == UserStatus.PendingMfa)
        {
            var setupToken = jwtService.GenerateMfaToken(user);
            return Ok(new
            {
                mfaRequired = false,
                mfaSetupRequired = true,
                message = "请先绑定 MFA",
                mfaToken = setupToken,
                expiresIn = 300
            });
        }

        var mfaToken = jwtService.GenerateMfaToken(user);

        return Ok(new
        {
            mfaRequired = true,
            mfaToken,
            expiresIn = 300
        });
    }

    [HttpPost("verify-mfa")]
    public async Task<IActionResult> VerifyMfa([FromBody] VerifyMfaRequest request, CancellationToken ct)
    {
        var principal = jwtService.ValidateMfaToken(request.MfaToken);
        if (principal is null)
            return this.Unauthorized("MFA Token 无效或已过期");

        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user is null)
            return this.Unauthorized("用户不存在");

        if (user.Status == UserStatus.Disabled)
            return this.Forbidden("用户已被禁用");

        if (!string.IsNullOrWhiteSpace(request.TotpCode))
        {
            if (string.IsNullOrWhiteSpace(user.MfaSecretEncrypted))
                return this.BadRequest("MFA 未配置");

            var secret = encryption.Decrypt(user.MfaSecretEncrypted);
            var isValid = mfaService.ValidateTotp(secret, request.TotpCode);
            if (!isValid)
                return this.Unauthorized("MFA 验证码错误");
        }
        else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            var recoveryCodes = JsonSerializer.Deserialize<List<string>>(user.RecoveryCodesJson) ?? [];
            var normalizedCode = request.RecoveryCode.Trim().ToUpperInvariant();
            var matchedIndex = recoveryCodes.FindIndex(c => c.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
            if (matchedIndex < 0)
                return this.Unauthorized("恢复码无效或已使用");

            recoveryCodes.RemoveAt(matchedIndex);
            user.RecoveryCodesJson = JsonSerializer.Serialize(recoveryCodes);
        }
        else
        {
            return this.BadRequest("请提供 TOTP 码或恢复码");
        }

        user.LastLoginAt = DateTime.UtcNow;
        if (user.Status == UserStatus.PendingMfa)
            user.Status = UserStatus.Active;

        await userRepo.UpdateAsync(user, ct);

        var accessToken = jwtService.GenerateAccessToken(user);
        var refreshTokenStr = jwtService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(jwtService.RefreshTokenExpirationDays)
        };
        await refreshTokenRepo.AddAsync(refreshToken, ct);

        return Ok(new
        {
            accessToken,
            refreshToken = refreshTokenStr,
            expiresIn = jwtService.AccessTokenExpirationSeconds,
            role = user.Role.ToString()
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await refreshTokenUseCase.ExecuteAsync(new RefreshTokenCommand(request.RefreshToken), ct);
        if (!result.Success)
            return this.Unauthorized(result.Error ?? "Refresh token 无效或已过期");

        var authResult = result.Data!;
        var user = await userRepo.GetByIdAsync(authResult.UserId, ct);
        if (user is null)
            return this.Unauthorized("用户不存在");

        var newAccessToken = jwtService.GenerateAccessToken(user);

        return Ok(new { accessToken = newAccessToken, refreshToken = authResult.RefreshToken, expiresIn = jwtService.AccessTokenExpirationSeconds });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await refreshTokenRepo.RevokeAllForUserAsync(userId, ct);
        return Ok(new { message = "已登出" });
    }

    [HttpPost("mfa/setup")]
    [Authorize]
    public async Task<IActionResult> SetupMfa(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user is null)
            return NotFound();

        var secret = mfaService.GenerateSecret(userId);
        var encryptedSecret = encryption.Encrypt(secret.SecretKey);
        user.MfaSecretEncrypted = encryptedSecret;

        var recoveryCodes = mfaService.GenerateRecoveryCodes(userId);
        user.RecoveryCodesJson = JsonSerializer.Serialize(recoveryCodes.Select(c => c.Code).ToList());

        await userRepo.UpdateAsync(user, ct);

        var otpauthUrl = $"otpauth://totp/TradeX:{user.Username}?secret={secret.SecretKey}&issuer=TradeX";

        // 使用 QRCoder 生成二维码 PNG 并转为 base64 data URL
        using var qr = new QRCodeGenerator();
        var qrData = qr.CreateQrCode(otpauthUrl, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(qrData);
        var pngBytes = png.GetGraphic(4);
        var qrBase64 = Convert.ToBase64String(pngBytes);

        return Ok(new MfaSetupResponse(
            secret.SecretKey,
            otpauthUrl,
            $"data:image/png;base64,{qrBase64}",
            recoveryCodes.Select(c => c.Code).ToList()
        ));
    }

    [HttpPost("mfa/verify")]
    [Authorize]
    public async Task<IActionResult> VerifyMfaSetup([FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await userRepo.GetByIdAsync(userId, ct);
        if (user is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(user.MfaSecretEncrypted))
            return this.BadRequest("请先调用 mfa/setup 生成密钥");

        var secret = encryption.Decrypt(user.MfaSecretEncrypted);
        if (!mfaService.ValidateTotp(secret, request.Code))
            return this.BadRequest("MFA 验证码错误");

        user.IsMfaEnabled = true;
        user.Status = UserStatus.Active;
        await userRepo.UpdateAsync(user, ct);

        var accessToken = jwtService.GenerateAccessToken(user);
        var refreshTokenStr = jwtService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(jwtService.RefreshTokenExpirationDays)
        };
        await refreshTokenRepo.AddAsync(refreshToken, ct);

        var recoveryCodes = JsonSerializer.Deserialize<List<string>>(user.RecoveryCodesJson) ?? [];

        return Ok(new
        {
            recoveryCodes,
            accessToken,
            refreshToken = refreshTokenStr,
            expiresIn = jwtService.AccessTokenExpirationSeconds
        });
    }

    [HttpPost("send-recovery-codes")]
    [Authorize]
    public async Task<IActionResult> SendRecoveryCodes([FromBody] SendRecoveryCodesRequest request, CancellationToken ct)
    {
        var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;

        // 仅 SuperAdmin/Admin 可为其他用户生成恢复码；普通用户只能为自己生成
        var isAdmin = callerRole is "SuperAdmin" or "Admin";
        if (!isAdmin && callerId != request.UserId)
            return this.Forbidden("无权为其他用户生成恢复码");

        var user = await userRepo.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return this.NotFound("用户不存在");

        var recoveryCodes = mfaService.GenerateRecoveryCodes(user.Id);
        user.RecoveryCodesJson = JsonSerializer.Serialize(recoveryCodes.Select(c => c.Code).ToList());
        await userRepo.UpdateAsync(user, ct);

        return Ok(new { recoveryCodes = recoveryCodes.Select(c => c.Code).ToList() });
    }
}
