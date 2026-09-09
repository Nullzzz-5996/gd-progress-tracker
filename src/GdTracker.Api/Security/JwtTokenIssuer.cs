using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GdTracker.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace GdTracker.Api.Security;

/// <summary>Выдача токенов доступа.</summary>
public interface ITokenIssuer
{
    /// <summary>Выпускает токен для аккаунта и сообщает момент его истечения.</summary>
    (string Token, DateTime ExpiresAtUtc) Issue(UserAccount user);
}

/// <summary>Выдача подписанных JWT (HMAC-SHA256).</summary>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly SigningCredentials _credentials;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _lifetime;

    public JwtTokenIssuer(byte[] signingKey, string issuer, string audience, TimeSpan lifetime)
    {
        _credentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256);
        _issuer = issuer;
        _audience = audience;
        _lifetime = lifetime;
    }

    public (string Token, DateTime ExpiresAtUtc) Issue(UserAccount user)
    {
        var expires = DateTime.UtcNow.Add(_lifetime);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                // Уникальный идентификатор токена: пригодится, если позже
                // понадобится отзыв конкретной сессии.
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: _credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
