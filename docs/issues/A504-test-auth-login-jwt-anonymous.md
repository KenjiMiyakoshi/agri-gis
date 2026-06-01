# A504: AuthLoginTests + JwtValidationTests + AnonymousReadTests

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 1d |
| Depends on | A205, A501, A502 |
| Blocks | なし |

## 概要
ログイン / JWT 検証 / 匿名読み取り（仕様確認）の 3 つのテストファイルを新規追加する。

## 背景・目的
採択案「案 P」のテストセクション:
> 新規テストファイル:
>   - `Tests/Auth/AuthLoginTests.cs`
>   - `Tests/Auth/JwtValidationTests.cs`
>   - `Tests/Auth/AnonymousReadTests.cs`

## スコープ
### 含む
#### AuthLoginTests
- 正常ログイン (alice) → 200, access_token + expires_at, JWT 内 claims 確認
- 不正 password → 401, body に詳細なし
- 存在しない login_id → 401（user 列挙不可）
- deleted_at IS NOT NULL の user → 401
- `GET /api/auth/me` を有効 JWT で → 200, claims に対応した user 情報
- `POST /api/auth/change-password` 正常 → 204, 新 password で login OK / 旧 password で 401
- change-password で現 password 不一致 → 400

#### JwtValidationTests
- 改竄 JWT (`WithTamperedToken`) → 401
- 期限切れ JWT (`WithExpiredToken`) → 401
- iss/aud 不一致の JWT（TokenForge で別 issuer を渡す） → 401
- HS256 以外のアルゴリズム → 401
- Authorization ヘッダフォーマット不正 (`Bearer xxx.yyy` ではない) → 401

#### AnonymousReadTests
- 採択案: guest = JWT 必須なので anonymous は GET 系も 401
- AllowAnonymous を明示したエンドポイント（あれば）は 200
- Phase A では `/api/auth/login` 自身のみが anonymous OK → これを確認

### 含まない
- 認可マトリクス (A505)
- Admin CRUD (A506)

## 受け入れ条件 (Acceptance Criteria)
- [ ] AuthLoginTests 全ケース green
- [ ] JwtValidationTests 全ケース green
- [ ] AnonymousReadTests: anonymous で `/api/features` GET → 401, `/api/auth/login` POST → 200
- [ ] テストは並列実行可能（DbReset がトランザクション境界）

## 影響ファイル
- `D:\proj\agri-gis\tests\Tests\Auth\AuthLoginTests.cs` (新規)
- `D:\proj\agri-gis\tests\Tests\Auth\JwtValidationTests.cs` (新規)
- `D:\proj\agri-gis\tests\Tests\Auth\AnonymousReadTests.cs` (新規)

## 実装ノート
```csharp
// AuthLoginTests.cs
[Fact]
public async Task Login_Valid_Returns_AccessToken()
{
    using var client = _factory.Anonymous();
    var res = await client.PostAsync("/api/auth/login",
        JsonContent.Create(new { login_id = "alice", password = SeedUsers.Alice.Password }));
    res.EnsureSuccessStatusCode();
    var body = await res.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(body);
    Assert.False(string.IsNullOrEmpty(body!.AccessToken));
    Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow.AddHours(7));

    // claims 確認
    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.AccessToken);
    Assert.Equal(SeedUsers.Alice.UserId.ToString(), jwt.Subject);
    Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "admin");
}

[Fact]
public async Task Login_BadPassword_Returns_401()
{
    using var client = _factory.Anonymous();
    var res = await client.PostAsync("/api/auth/login",
        JsonContent.Create(new { login_id = "alice", password = "WRONG" }));
    Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
}

// JwtValidationTests.cs
[Fact]
public async Task TamperedToken_Returns_401()
{
    var client = _factory.WithTamperedToken("alice");
    var res = await client.GetAsync("/api/auth/me");
    Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
}

[Fact]
public async Task ExpiredToken_Returns_401()
{
    var client = _factory.WithExpiredToken("alice");
    var res = await client.GetAsync("/api/auth/me");
    Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
}

// AnonymousReadTests.cs
[Fact]
public async Task Anonymous_Login_Endpoint_Returns_200()
{
    using var client = _factory.Anonymous();
    var res = await client.PostAsync("/api/auth/login",
        JsonContent.Create(new { login_id = "alice", password = SeedUsers.Alice.Password }));
    Assert.Equal(HttpStatusCode.OK, res.StatusCode);
}

[Fact]
public async Task Anonymous_Features_Returns_401()
{
    using var client = _factory.Anonymous();
    var res = await client.GetAsync("/api/features?layer_id=1");
    Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
}
```

注意点:
- JwtSecurityTokenHandler の signature validation 一致確認は別途 TokenForge の動作確認も兼ねる
- AnonymousReadTests は採択案の方針確認用なので軽め

## テスト観点
- Auth エンドポイントの正常系 / 異常系を網羅
- JWT 検証の各ケース
