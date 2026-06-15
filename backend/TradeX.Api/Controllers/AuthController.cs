using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;
using TradeX.Api.Services;
using TradeX.Application.Auth;
using TradeX.Application.Common;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController(
    IRefreshTokenRepository refreshTokenRepo,
    IEncryptionService encryption,
    JwtService jwtService,
    MfaService mfaService,
    IUseCase<LoginCommand, Result<AuthResultDto>> loginUseCase,
    IUseCase<RefreshTokenCommand, Result<AuthResultDto>> refreshTokenUseCase) : ControllerBase
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
        {
            return result.StatusCode switch
            {
                403 => this.Forbidden(result.Error!),
                _ => Unauthorized(new { message = result.Error })
            };
        }

        var dto = result.Data!;

        // MFA 流程
        if (dto.MfaSetupRequired || dto.MfaRequired)
        {
            return Ok(new
            {
                mfaRequired = dto.MfaRequired,
                mfaSetupRequired = dto.MfaSetupRequired,
                message = dto.Message,
                mfaToken = dto.MfaToken,
                expiresIn = 300
            });
        }

        // 直接登录成功
        return Ok(new
        {
            accessToken = dto.AccessToken,
            refreshToken = dto.RefreshToken,
            expiresIn = jwtService.AccessTokenExpirationSeconds,
            role = dto.Role
        });
    }

    [HttpPost("verify-mfa")]
    public async Task<IActionResult> VerifyMfa([FromBody] VerifyMfaRequest request, CancellationToken ct)
    {
        var principal = jwtService.ValidateMfaToken(request.MfaToken);
        if (principal is null)
            return this.Unauthorized("MFA Token 无效或已过期");

        var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var userRepo = HttpContext.RequestServices.GetRequiredService<IUserRepository>();
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

        var userRepo = HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = await userRepo.GetByIdAsync(result.Data!.UserId, ct);
        if (user is null)
            return this.Unauthorized("用户不存在");

        if (user.Status == UserStatus.Disabled)
            return this.Forbidden("用户已被禁用");

        var newAccessToken = jwtService.GenerateAccessToken(user);

        return Ok(new { accessToken = newAccessToken, refreshToken = result.Data.RefreshToken, expiresIn = jwtService.AccessTokenExpirationSeconds });
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
        var userRepo = HttpContext.RequestServices.GetRequiredService<IUserRepository>();
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
        var userRepo = HttpContext.RequestServices.GetRequiredService<IUserRepository>();
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

        var isAdmin = callerRole is "SuperAdmin" or "Admin";
        if (!isAdmin && callerId != request.UserId)
            return this.Forbidden("无权为其他用户生成恢复码");

        var userRepo = HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = await userRepo.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return this.NotFound("用户不存在");

        var recoveryCodes = mfaService.GenerateRecoveryCodes(user.Id);
        user.RecoveryCodesJson = JsonSerializer.Serialize(recoveryCodes.Select(c => c.Code).ToList());
        await userRepo.UpdateAsync(user, ct);

        return Ok(new { recoveryCodes = recoveryCodes.Select(c => c.Code).ToList() });
    }
}
