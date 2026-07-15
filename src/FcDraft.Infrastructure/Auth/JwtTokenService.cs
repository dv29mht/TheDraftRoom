using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FcDraft.Infrastructure.Auth;

public sealed class JwtTokenService(IConfiguration configuration) : ITokenService
{
    public TokenResult Create(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString().ToLowerInvariant()),
            new Claim(DraftClaimTypes.PasswordChangeRequired, user.MustChangePassword ? "true" : "false"),
            new Claim(DraftClaimTypes.SecurityStamp, user.SecurityStamp)
        };
        var jwt = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);
        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }
}
