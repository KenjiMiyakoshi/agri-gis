using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AgriGis.Api.Tests.Fixtures;

// テスト用に HS256 JWT を直接発行する。ApiFactory.TestJwtSecret/Issuer/Audience と一致させる。
public static class TokenForge
{
    public static string Issue(
        Guid userId,
        string loginId,
        string displayName,
        int orgId,
        IEnumerable<string> roles,
        TimeSpan? ttl = null,
        string? secret = null,
        string? issuer = null,
        string? audience = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret ?? ApiFactory.TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var effectiveTtl = ttl ?? TimeSpan.FromHours(1);
        // ttl が負なら「過去に発行済みで既に期限切れ」のトークンを作る
        DateTime nbf, exp;
        if (effectiveTtl <= TimeSpan.Zero)
        {
            exp = DateTime.UtcNow.Add(effectiveTtl);          // 過去
            nbf = exp.Subtract(TimeSpan.FromMinutes(5));      // expires より前
        }
        else
        {
            nbf = DateTime.UtcNow;
            exp = nbf.Add(effectiveTtl);
        }
        var now = nbf;

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

        var token = new JwtSecurityToken(
            issuer: issuer ?? ApiFactory.TestJwtIssuer,
            audience: audience ?? ApiFactory.TestJwtAudience,
            claims: claims,
            notBefore: now,
            expires: exp,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
