using System.Security.Cryptography;
using TradeX.Application.Common;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using static BCrypt.Net.BCrypt;

namespace TradeX.Application.Auth;

public sealed record AuthResultDto(
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    Guid UserId,
    string Username,
    string Role,
    bool MfaRequired,
    bool MfaSetupRequired,
    string? MfaToken,
    string? Message);

public sealed record LoginCommand(string Username, string Password);

/// <summary>
/// 登录用例 — 完整认证流程：密码验证 → 状态检查 → MFA 判断 → 令牌签发。
/// 消除控制器中的重复查询和令牌生成逻辑。
/// </summary>
public sealed class LoginUseCase(
    IUserRepository userRepo,
    IAuthTokenService tokenService) : IUseCase<LoginCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> ExecuteAsync(LoginCommand cmd, CancellationToken ct = default)
    {
        var user = await userRepo.GetByUsernameAsync(cmd.Username, ct);
        if (user is null)
            return Result<AuthResultDto>.BadRequest("用户名或密码错误");

        if (!Verify(cmd.Password, user.PasswordHash))
            return Result<AuthResultDto>.BadRequest("用户名或密码错误");

        if (user.Status == UserStatus.Disabled)
            return Result<AuthResultDto>.Forbidden("用户已被禁用");

        // MFA 未启用且状态为 PendingMfa → 提示绑定
        if (!user.IsMfaEnabled && user.Status == UserStatus.PendingMfa)
        {
            var mfaToken = tokenService.GenerateMfaToken(user);
            return Result<AuthResultDto>.Ok(new AuthResultDto(
                null, null, null,
                user.Id, user.Username, user.Role.ToString(),
                MfaRequired: false, MfaSetupRequired: true,
                MfaToken: mfaToken, Message: "请先绑定 MFA"));
        }

        // MFA 已启用 → 需要验证
        if (user.IsMfaEnabled)
        {
            var mfaToken = tokenService.GenerateMfaToken(user);
            return Result<AuthResultDto>.Ok(new AuthResultDto(
                null, null, null,
                user.Id, user.Username, user.Role.ToString(),
                MfaRequired: true, MfaSetupRequired: false,
                MfaToken: mfaToken, Message: null));
        }

        // 无 MFA → 直接签发令牌
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        return Result<AuthResultDto>.Ok(new AuthResultDto(
            accessToken, refreshToken, DateTime.UtcNow.AddDays(tokenService.RefreshTokenExpirationDays),
            user.Id, user.Username, user.Role.ToString(),
            MfaRequired: false, MfaSetupRequired: false,
            MfaToken: null, Message: null));
    }
}

public sealed record RefreshTokenCommand(string RefreshToken);

/// <summary>刷新令牌用例 — 验证 RefreshToken 并返回用户信息。令牌签发由 Controller 完成。</summary>
public sealed class RefreshTokenUseCase(
    IUserRepository userRepo,
    IRefreshTokenRepository refreshTokenRepo) : IUseCase<RefreshTokenCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> ExecuteAsync(RefreshTokenCommand cmd, CancellationToken ct = default)
    {
        var storedToken = await refreshTokenRepo.GetByTokenAsync(cmd.RefreshToken, ct);
        if (storedToken is null || storedToken.IsExpired || storedToken.IsRevoked)
            return Result<AuthResultDto>.BadRequest("Refresh token 无效或已过期");

        var user = await userRepo.GetByIdAsync(storedToken.UserId, ct);
        if (user is null)
            return Result<AuthResultDto>.BadRequest("用户不存在");

        if (user.Status == UserStatus.Disabled)
            return Result<AuthResultDto>.Forbidden("用户已被禁用");

        // 吊销旧 token
        storedToken.RevokedAt = DateTime.UtcNow;

        // 生成新 refresh token
        var newRefreshTokenStr = GenerateRefreshToken();
        var newRefreshToken = new Core.Models.RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        await refreshTokenRepo.AddAsync(newRefreshToken, ct);

        return Result<AuthResultDto>.Ok(new AuthResultDto(
            null, newRefreshTokenStr, DateTime.UtcNow.AddDays(7),
            user.Id, user.Username, user.Role.ToString(),
            MfaRequired: false, MfaSetupRequired: false,
            MfaToken: null, Message: null));
    }

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = global::System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}

public sealed record GetCurrentUserQuery(Guid UserId);

/// <summary>获取当前用户信息用例。</summary>
public sealed class GetCurrentUserUseCase(
    IUserRepository userRepo) : IUseCase<GetCurrentUserQuery, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> ExecuteAsync(GetCurrentUserQuery query, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(query.UserId, ct);
        if (user is null)
            return Result<AuthResultDto>.NotFound("用户不存在");

        return Result<AuthResultDto>.Ok(new AuthResultDto(
            null, null, null,
            user.Id, user.Username, user.Role.ToString(),
            MfaRequired: false, MfaSetupRequired: false,
            MfaToken: null, Message: null));
    }
}
