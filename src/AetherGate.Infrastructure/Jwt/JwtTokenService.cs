using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AetherGate.Application.Actors;
using Microsoft.IdentityModel.Tokens;

namespace AetherGate.Infrastructure.Jwt;

/// <summary>
/// 실제 JWT 발급 / 검증 구현체.
/// AuthActor 생성 시 DI로 주입하여 DefaultTokenService 대체.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryHours;

    public JwtTokenService(string secret, string issuer = "AetherGate",
        string audience = "AetherGateClient", int expiryHours = 24)
    {
        _secret = secret;
        _issuer = issuer;
        _audience = audience;
        _expiryHours = expiryHours;
    }

    public string GenerateToken(string playerId, string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, playerId),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_expiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool ValidateToken(string token, out string playerId)
    {
        playerId = string.Empty;
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));

        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            playerId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                       ?? string.Empty;
            return !string.IsNullOrEmpty(playerId);
        }
        catch
        {
            return false;
        }
    }
}
