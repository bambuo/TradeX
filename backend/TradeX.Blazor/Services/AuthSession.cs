namespace TradeX.Blazor.Services;

public sealed class AuthSession
{
    public string? MfaToken { get; private set; }

    public void SetMfaToken(string mfaToken)
    {
        MfaToken = mfaToken;
    }

    public void Clear()
    {
        MfaToken = null;
    }
}

public sealed record AuthTokens(string AccessToken, string RefreshToken, Guid UserId, string UserName, string Role);
