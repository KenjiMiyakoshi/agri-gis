using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AgriGis.Api.Auth;

// IHttpContextAccessor から ClaimsPrincipal を取り出し、JWT claims をプロパティ化する。
// claim キーは JwtService.IssueAccessToken 側の発行値と対応:
//   - sub        → UserId (Guid)
//   - login_id   → LoginId
//   - display_name → DisplayName
//   - org_id     → OrgId (int)
//   - ClaimTypes.Role (複数可) → Roles
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var g) ? g : Guid.Empty;
        }
    }

    public string LoginId => Principal?.FindFirstValue("login_id") ?? "";
    public string DisplayName => Principal?.FindFirstValue("display_name") ?? "";

    public int OrgId
    {
        get
        {
            var raw = Principal?.FindFirstValue("org_id");
            return int.TryParse(raw, out var i) ? i : 0;
        }
    }

    public IReadOnlyList<string> Roles
    {
        get
        {
            if (Principal is null) return Array.Empty<string>();
            return Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        }
    }

    public bool HasRole(string role) =>
        Principal?.IsInRole(role) ?? false;

    // D103 (WD1): sid_session claim を Guid 化。欠落時は Guid.Empty。
    // 既発行 (Phase A/B/C 期) token は claim を持たないため OnTokenValidated で弾かれる前提。
    public Guid SessionId
    {
        get
        {
            var raw = Principal?.FindFirstValue("sid_session");
            return Guid.TryParse(raw, out var g) ? g : Guid.Empty;
        }
    }
}
