// Issues the access token of a successful login (Stage 2, phase "TokenService").
//
// The claims are exactly the two the controllers read back - never more:
//   ClaimTypes.NameIdentifier -> the Guid of the authenticated User. Every endpoint takes its
//     identity (employeeId / currentUserId / editorId / actorId) from this claim, never from a body.
//   ClaimTypes.Role           -> the UserRole name, consumed by the authorization policies.
// ClaimTypes.Name carries the login for display/logging; it takes no part in decisions.
//
// Lifetime is BR32: 8 hours (JwtSettings.ExpiryMinutes, default 480). The token is signed with
// HS256 over the same settings singleton that JwtBearer validation uses in Program.cs.
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using WorkTime.Domain;

namespace WorkTime.Api;

public class TokenService
{
    private readonly JwtSettings _settings;

    public TokenService(JwtSettings settings)
    {
        _settings = settings;
    }

    public Task<string> TryIssueAccessToken(Guid userId, UserRole role, string login, CancellationToken cancellationToken)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_settings.ExpiryMinutes);

        var signingKey = new SymmetricSecurityKey(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(_settings.SecretKey)));
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(ClaimTypes.Name, login),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = issuedAt,
            Expires = expiresAt,
            SigningCredentials = signingCredentials,
        };

        var token = new JwtSecurityTokenHandler().CreateToken(descriptor);

        return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
    }

    // Expiry of the token just issued, in seconds - the "expiresIn" field of a login response.
    public long TryExpiresInSeconds()
    {
        return _settings.ExpiryMinutes * 60L;
    }
}

// JwtBearer wiring of the same settings: issuer, audience, lifetime and the HS256 key are all
// validated, with the claim names the token above carries (Stage 2, phase "authentication").
public class JwtBearerSetup
{
    // Small clock skew: the server and the client are one clock, drift tolerance only.
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    public static void TryConfigure(
        Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions options,
        JwtSettings settings)
    {
        var signingKey = new SymmetricSecurityKey(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(settings.SecretKey)));

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ClockSkew = ClockSkew,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
    }
}
