# A506: AdminUsersCrudTests + AdminOrgsCrudTests

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 1d |
| Depends on | A301, A302, A502 |
| Blocks | なし |

## 概要
Admin の Users / Organizations CRUD の機能テストを 2 ファイルで実装する。

## 背景・目的
採択案「案 P」のテストセクション:
> `Tests/Admin/AdminUsersCrudTests.cs`
> `Tests/Admin/AdminOrgsCrudTests.cs`

## スコープ
### 含む
#### AdminUsersCrudTests
- POST: 正常作成 → 201 + Location、DB に user + user_roles
- POST: 重複 login_id → 409
- POST: 無効 role 値 → 400
- POST: password < 8 文字 → 400
- POST: 不在 org_id → 400 (FK)
- GET list: deleted_at 含めない / `?include_deleted=true` で含める
- GET one: 存在 → 200 / 不在 → 404 / 論理削除済 → 404
- PUT: roles 更新（DELETE + INSERT で同期確認）
- PUT password: 新 hash 反映、新 password で login OK / 旧 password で 401
- DELETE: 論理削除確認 (deleted_at NOT NULL)
- DELETE 後、同一 login_id で再 POST → 201
- self-delete 試行（admin が自分を消す）→ 400 / 403（実装した防御に合わせる）

#### AdminOrgsCrudTests
- POST: 正常 → 201
- POST: 重複 code → 409
- PUT: 更新 → 204
- DELETE: 論理削除 → 204
- DELETE: ひもづく user あり → 409
- 論理削除後、同一 code で再 POST → 201
- GET list: include_deleted の動作

### 含まない
- 認可マトリクス (A505)
- audit_log 観点 (A507)

## 受け入れ条件 (Acceptance Criteria)
- [ ] 上記ケース全 green
- [ ] DbReset で seed されたユーザを admin 操作対象として使用可能
- [ ] `WithActorAs("alice", "admin")` で全エンドポイント実行
- [ ] エラー応答はすべて `application/problem+json`

## 影響ファイル
- `D:\proj\agri-gis\tests\Tests\Admin\AdminUsersCrudTests.cs` (新規)
- `D:\proj\agri-gis\tests\Tests\Admin\AdminOrgsCrudTests.cs` (新規)

## 実装ノート
```csharp
// AdminUsersCrudTests.cs
public class AdminUsersCrudTests : IClassFixture<ApiFactory>
{
    private readonly ApiClientFactory _factory;
    public AdminUsersCrudTests(ApiFactory api) { _factory = new(api); }

    [Fact]
    public async Task CreateUser_NewLogin_Returns_201()
    {
        var client = _factory.WithActorAs("alice", "admin");
        var res = await client.PostAsync("/api/admin/users",
            JsonContent.Create(new
            {
                login_id = "newuser",
                display_name = "New User",
                password = "newpass123",
                org_id = SeedUsers.OrgId,
                roles = new[] { "general" }
            }));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task DeleteAndRecreate_Same_LoginId_Works()
    {
        var client = _factory.WithActorAs("alice", "admin");
        // bob を論理削除
        var del = await client.DeleteAsync($"/api/admin/users/{SeedUsers.Bob.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        // 同じ login_id で再作成
        var post = await client.PostAsync("/api/admin/users",
            JsonContent.Create(new
            {
                login_id = "bob",
                display_name = "Bob 2",
                password = "bob2pass",
                org_id = SeedUsers.OrgId,
                roles = new[] { "general" }
            }));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_AllowsLogin_With_NewPassword()
    {
        var admin = _factory.WithActorAs("alice", "admin");
        var res = await admin.PutAsync(
            $"/api/admin/users/{SeedUsers.Bob.UserId}/password",
            JsonContent.Create(new { new_password = "bob-new-pass" }));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var anon = _factory.Anonymous();
        var login = await anon.PostAsync("/api/auth/login",
            JsonContent.Create(new { login_id = "bob", password = "bob-new-pass" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
```

注意点:
- ResetPassword テストは A205 (login) と連携、SeedUsers のパスワードを上書きするのでテスト並列性に注意 → ApiFactory のシード再投入を確実に
- self-delete 防止の挙動はテスト中に明文化

## テスト観点
- Admin CRUD の正常系/異常系完備
- 論理削除 + 部分 UNIQUE の動作確認
