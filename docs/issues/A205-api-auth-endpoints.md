# A205: POST /api/auth/login + GET /api/auth/me + POST /api/auth/change-password

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 1d |
| Depends on | A201, A202, A204 |
| Blocks | A402, A403, A504 |

## 概要
新規 `AuthEndpoints.cs` を作り、ログイン / 自分情報取得 / パスワード変更の 3 エンドポイントを実装する。

## 背景・目的
採択案「案 P」の API セクション:
> 新規 `AuthEndpoints.cs`: `POST /api/auth/login`, `GET /api/auth/me`
> 本人パスワード変更: `POST /api/auth/change-password`

## スコープ
### 含む
- `POST /api/auth/login`: body `{ "login_id": "...", "password": "..." }` → `{ "access_token": "...", "expires_at": "..." }`
- `GET /api/auth/me` [Authorize]: ICurrentUser から `{ user_id, login_id, display_name, org_id, roles[] }`
- `POST /api/auth/change-password` [Authorize]: body `{ "current_password": "...", "new_password": "..." }` → 204
- JWT 発行ヘルパ `JwtIssuer.cs`: HS256, claims (sub/name/role 複数/org_id/iss/aud/exp/iat/jti/display_name)
- BCrypt.Verify でパスワード検証
- 失敗時 401 (login_id 不在 / password 不一致いずれも同じ応答、ユーザ列挙対策)

### 含まない
- refresh token (Phase B)
- パスワード強度ポリシー（Phase B、ここでは最低 8 文字のみ）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `POST /api/auth/login` 正常: 200, body に access_token (JWT) と expires_at (UTC ISO8601)
- [ ] login_id 不在: 401, body に詳細なし（"invalid credentials" のみ）
- [ ] password 不一致: 401, 同上
- [ ] deleted_at IS NOT NULL のユーザは login 不可: 401
- [ ] JWT を Authorization: Bearer で `GET /api/auth/me` → 200, body に user_id (UUID), roles[]
- [ ] `POST /api/auth/change-password` 現パスワード不一致: 400 ProblemDetails
- [ ] パスワード変更後、新 password で login 成功、旧 password で 401
- [ ] 新パスワード 8 文字未満: 400

## 影響ファイル
- `D:\proj\agri-gis\api\Auth\AuthEndpoints.cs` (新規)
- `D:\proj\agri-gis\api\Auth\JwtIssuer.cs` (新規)
- `D:\proj\agri-gis\api\Auth\AuthDtos.cs` (新規: LoginRequest, LoginResponse, ChangePasswordRequest, MeResponse)
- `D:\proj\agri-gis\api\Program.cs` (MapGroup 登録)

## 実装ノート
```csharp
// AuthEndpoints.cs
public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (LoginRequest req, NpgsqlConnection db, JwtIssuer issuer) =>
        {
            await db.OpenAsync();
            var u = await db.QuerySingleOrDefaultAsync<UserRow>(@"
                SELECT u.user_id, u.login_id, u.display_name, u.password_hash, u.org_id,
                       array_agg(r.role ORDER BY r.role) AS roles
                FROM users u
                LEFT JOIN user_roles r ON r.user_id = u.user_id
                WHERE u.login_id = @login_id AND u.deleted_at IS NULL
                GROUP BY u.user_id", new { req.login_id });
            if (u is null || !BCrypt.Net.BCrypt.Verify(req.password, u.password_hash))
                return Results.Json(new { error = "invalid_credentials" }, statusCode: 401);

            var (token, expiresAt) = issuer.Issue(u);
            return Results.Ok(new { access_token = token, expires_at = expiresAt });
        }).AllowAnonymous();

        g.MapGet("/me", (ICurrentUser me) => Results.Ok(new
        {
            user_id = me.UserId, login_id = me.LoginId,
            display_name = me.DisplayName, org_id = me.OrgId, roles = me.Roles
        })).RequireAuthorization();

        g.MapPost("/change-password",
            async (ChangePasswordRequest req, ICurrentUser me, NpgsqlConnection db) =>
        {
            if (req.new_password is null || req.new_password.Length < 8)
                return Results.Problem("new_password must be >= 8 chars", statusCode: 400);
            await db.OpenAsync();
            var hash = await db.ExecuteScalarAsync<string>(
                "SELECT password_hash FROM users WHERE user_id=@id", new { id = me.UserId });
            if (!BCrypt.Net.BCrypt.Verify(req.current_password, hash))
                return Results.Problem("current_password mismatch", statusCode: 400);
            var newHash = BCrypt.Net.BCrypt.HashPassword(req.new_password, workFactor: 11);
            await db.ExecuteAsync(
                "UPDATE users SET password_hash=@h, updated_at=now() WHERE user_id=@id",
                new { h = newHash, id = me.UserId });
            return Results.NoContent();
        }).RequireAuthorization();
    }
}

// JwtIssuer.cs
public sealed class JwtIssuer
{
    private readonly JwtOptions _opts;
    public JwtIssuer(JwtOptions opts) { _opts = opts; }

    public (string token, DateTimeOffset expiresAt) Issue(UserRow u)
    {
        var now = DateTimeOffset.UtcNow;
        var exp = now.AddMinutes(_opts.AccessTokenLifetimeMinutes);
        var claims = new List<Claim>
        {
            new("sub", u.user_id.ToString()),
            new("name", u.login_id),
            new("display_name", u.display_name),
            new("org_id", u.org_id.ToString()),
            new("jti", Guid.NewGuid().ToString()),
        };
        foreach (var role in u.roles) claims.Add(new("role", role));
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(_opts.SecretBytes), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_opts.Issuer, _opts.Audience, claims,
            now.UtcDateTime, exp.UtcDateTime, creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), exp);
    }
}
```

注意点:
- ユーザ列挙対策: login_id 不在と password 不一致を区別しない
- BCrypt work factor 11 (採択案明示)
- `roles` は CHECK 制約で `admin|general|guest` のいずれかなので array_agg 結果はそのまま JWT に

## テスト観点
- A504 (AuthLoginTests): 正常ログイン、各種失敗、JWT 形式検証
- A504 (JwtValidationTests): claims 内容、exp = now+8h
- A506 (AdminUsersCrudTests) で password 変更後の login 成功は A205 内でカバー
