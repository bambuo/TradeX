using System.Security.Cryptography;
using TradeX.Application.Common;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using static BCrypt.Net.BCrypt;

namespace TradeX.Application.Auth;

public sealed record AuthResultDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Username,
    string Role);

public sealed record LoginCommand(string Username, string Password);

/// <summary>登录用例 — 验证用户凭据并返回身份信息。</summary>
public sealed class LoginUseCase(
    IUserRepository userRepo) : IUseCase<LoginCommand, Result<AuthResultDto>>
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

        return Result<AuthResultDto>.Ok(new AuthResultDto(
            string.Empty, string.Empty, DateTime.UtcNow,
            user.Id, user.Username, user.Role.ToString()));
    }
}

public sealed record RefreshTokenCommand(string RefreshToken);

/// <summary>刷新令牌用例 — 验证 RefreshToken 并签发新的令牌对。</summary>
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

        // 吊销旧 token
        storedToken.RevokedAt = DateTime.UtcNow;

        // 生成新 refresh token
        var newRefreshTokenStr = GenerateRefreshToken();
        var newRefreshToken = new Core.Models.RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(7) // 默认 7 天
        };
        await refreshTokenRepo.AddAsync(newRefreshToken, ct);

        return Result<AuthResultDto>.Ok(new AuthResultDto(
            string.Empty, newRefreshTokenStr, DateTime.UtcNow.AddDays(7),
            user.Id, user.Username, user.Role.ToString()));
    }

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
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
            string.Empty, string.Empty, DateTime.UtcNow,
            user.Id, user.Username, user.Role.ToString()));
    }
}
