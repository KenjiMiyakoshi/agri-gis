# A505: AuthorizationTests (3 role × エンドポイント matrix)

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 1d |
| Depends on | A206, A502 |
| Blocks | なし |

## 概要
admin / general / guest × 主要エンドポイント (Features, Layers, LayerSchema, Admin) の認可マトリクスを `AuthorizationTests.cs` で網羅する。`WithActorAs` 明示形を必ず使う。

## 背景・目的
採択案「案 P」のテストセクション:
> `Tests/Auth/AuthorizationTests.cs`（3 role × 各エンドポイント matrix）
> AuthorizationTests 系では必ず明示形を使う規約

## スコープ
### 含む
- 各エンドポイントに対し 3 role + anonymous で叩き、期待 status を確認
- マトリクス例:

| Endpoint                                    | admin | general | guest | anon |
|---------------------------------------------|-------|---------|-------|------|
| GET    /api/layers                          | 200   | 200     | 200   | 401  |
| GET    /api/features?layer_id=X             | 200   | 200     | 200   | 401  |
| POST   /api/features                        | 201   | 201     | 403   | 401  |
| PATCH  /api/features/{id}                   | 200/204 | 200/204 | 403 | 401  |
| DELETE /api/features/{id}                   | 204   | 204     | 403   | 401  |
| PUT    /api/layers/{id}/schema              | 200/204 | 200/204 | 403 | 401  |
| GET    /api/admin/organizations             | 200   | 403     | 403   | 401  |
| POST   /api/admin/organizations             | 201   | 403     | 403   | 401  |
| GET    /api/admin/users                     | 200   | 403     | 403   | 401  |
| POST   /api/admin/users                     | 201   | 403     | 403   | 401  |
| PUT    /api/admin/users/{id}/password       | 204   | 403     | 403   | 401  |

- `[Theory] [InlineData]` でパラメタライズ
- `WithActorAs("alice", role)` で role を明示注入

### 含まない
- 個別エンドポイントの機能テスト (それぞれ別ファイル)
- Phase A 範囲外のエンドポイント

## 受け入れ条件 (Acceptance Criteria)
- [ ] マトリクス全セル green
- [ ] 403 と 401 の区別が正しい
- [ ] 403 / 401 ともに `Content-Type: application/problem+json`
- [ ] `WithActorAs` を必ず使い、`WithActor` は AuthorizationTests 内で禁止

## 影響ファイル
- `D:\proj\agri-gis\tests\Tests\Auth\AuthorizationTests.cs` (新規)

## 実装ノート
```csharp
public class AuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiClientFactory _factory;
    public AuthorizationTests(ApiFactory api) { _factory = new(api); }

    public static IEnumerable<object[]> WriteMatrix() => new[]
    {
        new object[] { "admin",   HttpStatusCode.Created },
        new object[] { "general", HttpStatusCode.Created },
        new object[] { "guest",   HttpStatusCode.Forbidden },
    };

    [Theory, MemberData(nameof(WriteMatrix))]
    public async Task Post_Feature_Matrix(string role, HttpStatusCode expected)
    {
        var client = _factory.WithActorAs("alice", role);
        var res = await client.PostFeatureRaw(new { /* valid body */ });
        Assert.Equal(expected, res.StatusCode);
    }

    [Theory]
    [InlineData("admin",   HttpStatusCode.OK)]
    [InlineData("general", HttpStatusCode.Forbidden)]
    [InlineData("guest",   HttpStatusCode.Forbidden)]
    public async Task Get_AdminUsers_Matrix(string role, HttpStatusCode expected)
    {
        var client = _factory.WithActorAs("alice", role);
        var res = await client.GetAsync("/api/admin/users");
        Assert.Equal(expected, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_All_Endpoints_Return_401()
    {
        using var client = _factory.Anonymous();
        var endpoints = new (HttpMethod, string)[]
        {
            (HttpMethod.Get,    "/api/features?layer_id=1"),
            (HttpMethod.Post,   "/api/features"),
            (HttpMethod.Get,    "/api/admin/users"),
            // ...
        };
        foreach (var (m, url) in endpoints)
        {
            var res = await client.SendAsync(new HttpRequestMessage(m, url));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
    }
}
```

注意点:
- `WithActorAs("alice", "guest")` は「alice の user_id を持つが role=guest」という JWT を作る → 認可ロジックの純粋性テスト
- guest が自分の操作で audit_log に actor_user_id=alice が記録されるが、本テストでは status のみ確認

## テスト観点
- 認可ポリシーの正確性
- 漏れない実装かを Theory + マトリクスで強制
