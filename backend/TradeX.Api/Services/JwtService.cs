using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TradeX.Api.Settings;
using TradeX.Core.Models;

namespace TradeX.Api.Services;

public class JwtService(IOptions<JwtSettings> jwtSettings)
{
    public int AccessTokenExpirationSeconds => jwtSettings.Value.AccessTokenExpirationMinutes * 60;
    public int RefreshTokenExpirationDays => jwtSettings.Value.RefreshTokenExpirationDays;

    public string GenerateAccessToken(User user)
    {
        var settings = jwtSettings.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("mfa", user.IsMfaEnabled.ToString().ToLower())
        };

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(settings.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateMfaToken(User user)
    {
        var settings = jwtSettings.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("purpose", "mfa_verification")
        };

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateMfaToken(string token)
    {
        var settings = jwtSettings.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero
            }, out _);

            var purpose = principal.FindFirst("purpose")?.Value;
            if (purpose != "mfa_verification")
                return null;

            return principal;
        }
        catch
        {
            return null;
        }
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}
