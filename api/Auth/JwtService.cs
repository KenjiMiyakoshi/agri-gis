using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AgriGis.Api.Auth;

public sealed class JwtService
{
    private readonly byte[] _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _ttl;

    public JwtService(IConfiguration config)
    {
        var rawSecret = Environment.GetEnvironmentVariable("AGRI_GIS_JWT_SECRET");
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new InvalidOperationException(
                "AGRI_GIS_JWT_SECRET environment variable is not set. Required for JWT signing (HS256, min 32 bytes).");
        }
        _secret = Encoding.UTF8.GetBytes(rawSecret);
        if (_secret.Length < 32)
        {
            throw new InvalidOperationException(
                $"AGRI_GIS_JWT_SECRET must be at least 32 bytes (got {_secret.Length}).");
        }

        _issuer = Environment.GetEnvironmentVariable("AGRI_GIS_JWT_ISSUER")
                  ?? config["Jwt:Issuer"]
                  ?? "agri-gis-api";
        _audience = Environment.GetEnvironmentVariable("AGRI_GIS_JWT_AUDIENCE")
                    ?? config["Jwt:Audience"]
                    ?? "agri-gis-windows";

        var ttlHours = int.TryParse(
            Environment.GetEnvironmentVariable("AGRI_GIS_JWT_TTL_HOURS")
            ?? config["Jwt:ExpiryHours"], out var h) ? h : 8;
        _ttl = TimeSpan.FromHours(ttlHours);
    }

    public string Issuer => _issuer;
    public string Audience => _audience;
    public byte[] SecretBytes => _secret;
    public TimeSpan Ttl => _ttl;

    public (string token, DateTime expiresAt) IssueAccessToken(
        Guid userId,
        string loginId,
        string displayName,
        int orgId,
        IReadOnlyList<string> roles)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(_ttl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("login_id", loginId),
            new("display_name", displayName),
            new("org_id", orgId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        var key = new SymmetricSecurityKey(_secret);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
