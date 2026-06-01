# A502: TokenForge + ApiClientFactory.WithActorAs 明示形

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 0.5d |
| Depends on | A201, A501 |
| Blocks | A503, A504, A505, A506, A507, A508 |

## 概要
新規 `Fixtures/TokenForge.cs` で HS256 JWT を発行し、`ApiClientFactory` に `WithActorAs(loginId, role)` を追加。既存 `WithActor(loginId)` は SeedUsers の role に基づく糖衣として残置。

## 背景・目的
採択案「案 P」のテストセクション:
> 既存 `ApiClientFactory.WithActor("alice")` は糖衣として残置（DbReset の seed と一致する role）
> **新規 `ApiClientFactory.WithActorAs("alice", role: "admin")` 明示形を追加**、AuthorizationTests 系では必ず明示形を使う規約
> 新規 `Fixtures/TokenForge.cs`（HS256 で JWT 発行、API と同じ secret を環境変数経由で共有）

## スコープ
### 含む
- `Fixtures/TokenForge.cs`: API と同じ `AGRI_GIS_JWT_SECRET` を使い、HS256 で JWT 発行
  - メソッド: `Issue(Guid userId, string loginId, string displayName, int orgId, string[] roles, TimeSpan? lifetime = null)`
  - exp/iat/iss/aud/jti を含む
- `ApiClientFactory.WithActor(loginId)`: SeedUsers から user を引いて全 role を持つ JWT を発行（既存呼び出し互換）
- `ApiClientFactory.WithActorAs(loginId, params string[] roles)`: 明示 role 指定で JWT 発行（SeedUsers の role を上書き、AuthorizationTests 用）
- `ApiClientFactory.WithExpiredToken(loginId)`: 期限切れ JWT (JwtValidationTests 用)
- `ApiClientFactory.WithTamperedToken(loginId)`: 改竄 JWT (JwtValidationTests 用)
- `ApiClientFactory.Anonymous()`: Authorization 無し

### 含まない
- 既存テストの移行 (A503)
- 新規テスト (A504〜)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `TokenForge.Issue(...)` が API で検証可能な JWT を返す
- [ ] `WithActor("alice")` が SeedUsers.Alice の role (admin) を持つ JWT を発行
- [ ] `WithActorAs("alice", "guest")` で alice の user_id + guest role の JWT を発行（AuthorizationTests 用）
- [ ] `WithExpiredToken("alice")` の token は API で 401
- [ ] `WithTamperedToken("alice")` の token は API で 401
- [ ] `Anonymous()` は Authorization ヘッダ無し

## 影響ファイル
- `D:\proj\agri-gis\tests\Fixtures\TokenForge.cs` (新規)
- `D:\proj\agri-gis\tests\Fixtures\ApiClientFactory.cs` (拡張)

## 実装ノート
```csharp
// Fixtures/TokenForge.cs
public static class TokenForge
{
    private static readonly byte[] Secret =
        Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("AGRI_GIS_JWT_SECRET")
            ?? throw new InvalidOperationException("AGRI_GIS_JWT_SECRET required"));
    private const string Issuer   = "agri-gis-test";
    private const string Audience = "agri-gis-test";

    public static string Issue(
        Guid userId, string loginId, string displayName,
        int orgId, string[] roles, TimeSpan? lifetime = null)
    {
        var now = DateTimeOffset.UtcNow;
        var exp = now.Add(lifetime ?? TimeSpan.FromHours(8));
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("name", loginId),
            new("display_name", displayName),
            new("org_id", orgId.ToString()),
            new("jti", Guid.NewGuid().ToString()),
        };
        foreach (var r in roles) claims.Add(new("role", r));
        var creds = new SigningCredentials(new SymmetricSecurityKey(Secret), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(Issuer, Audience, claims, now.UtcDateTime, exp.UtcDateTime, creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

// Fixtures/ApiClientFactory.cs (拡張)
public ApiClient WithActor(string loginId)
{
    var u = SeedUsers.All.Single(x => x.LoginId == loginId);
    return WithToken(TokenForge.Issue(u.UserId, u.LoginId, u.DisplayName, SeedUsers.OrgId, u.Roles));
}

public ApiClient WithActorAs(string loginId, params string[] roles)
{
    var u = SeedUsers.All.Single(x => x.LoginId == loginId);
    return WithToken(TokenForge.Issue(u.UserId, u.LoginId, u.DisplayName, SeedUsers.OrgId, roles));
}

public ApiClient WithExpiredToken(string loginId)
{
    var u = SeedUsers.All.Single(x => x.LoginId == loginId);
    return WithToken(TokenForge.Issue(u.UserId, u.LoginId, u.DisplayName, SeedUsers.OrgId, u.Roles,
        lifetime: TimeSpan.FromSeconds(-60)));
}

public ApiClient WithTamperedToken(string loginId)
{
    var t = WithActor(loginId);
    var token = /* 取得方法は HttpClient.DefaultRequestHeaders から */;
    var parts = token.Split('.');
    parts[2] = "tampered_signature_xxxxxxxxxxxx";
    return WithToken(string.Join(".", parts));
}

public ApiClient Anonymous() => WithToken(null);

private ApiClient WithToken(string? token)
{
    var http = _factory.CreateClient();
    if (token is not null)
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
    return new ApiClient(http);
}
```

注意点:
- `AGRI_GIS_JWT_SECRET` はテスト実行時に必ず注入される必要がある（CI/local どちらも）。test bootstrap で `Environment.SetEnvironmentVariable(...)` を呼ぶか、`launchSettings.json` 的な仕組みで
- `Jwt:Issuer` / `Jwt:Audience` も API と同じ値をテスト config で

## テスト観点
- A504/A505 で使用される基盤
- TokenForge 単体テスト: 発行した JWT が JwtSecurityTokenHandler で読み戻せる
