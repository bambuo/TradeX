using TradeX.Core.Models;

namespace TradeX.Application.Common;

/// <summary>令牌生成服务 — 解耦 JWT/RefreshToken 生成逻辑，使 UseCase 可跨层调用。</summary>
public interface IAuthTokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    int AccessTokenExpirationSeconds { get; }
    int RefreshTokenExpirationDays { get; }
    string GenerateMfaToken(User user);
}
