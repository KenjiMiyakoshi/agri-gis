# A508: C1RegressionTests + BcryptHashTests + InitialAdminSeedTests

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 1d |
| Depends on | A104, A201, A207, A502 |
| Blocks | なし |

## 概要
C1 (半開区間接合) の回帰テスト、BCrypt パスワード検証、初期 admin seed の起動時挙動を確認する 3 ファイル。

## 背景・目的
採択案「案 P」のテストセクション:
> `Tests/Bitemporal/C1RegressionTests.cs`（C1 回帰、手動 UPDATE 削除済）
> `Tests/Auth/BcryptHashTests.cs`
> `Tests/Auth/InitialAdminSeedTests.cs`

## スコープ
### 含む
#### C1RegressionTests
- 当日 INSERT → 当日 UPDATE → 当日 asOf で新 current のみ返却
- 当日 INSERT → 当日 UPDATE → 過去日 asOf で 空（INSERT 自身の valid_from が今日のため）
- 過去日 INSERT (CURRENT_DATE 直接いじれないので migration の `SET valid_from = ...` で偽装、または時刻 mock) → 当日 UPDATE → 過去日 asOf で旧 history、今日 asOf で新 current
- 同日 2 回 UPDATE → history に 2 行、ゼロ幅区間が存在しても asOf テストは正常
- 手動 UPDATE を一切しない（A503 で削除済を確認）

#### BcryptHashTests
- API で admin 作成 → users.password_hash が `$2a$11$...` 形式
- work factor 11 を文字列パターンで確認
- 異なる password で 2 ユーザ作成 → hash が異なる
- 同 password でも salt 異なれば hash 異なる

#### InitialAdminSeedTests
- `AGRI_GIS_INITIAL_ADMIN_PW` 未設定で WebApplicationFactory 起動 → 起動失敗 (InvalidOperationException)
- 設定済で起動 → users に admin row、user_roles に admin
- 起動 2 回目で password を変更 → password_hash 更新（同じ password なら冪等）
- `AGRI_GIS_INITIAL_ADMIN_LOGIN_ID` カスタム名で起動 → そのユーザが作成される

### 含まない
- C2 回帰 (A507)
- audit_user_id (A507)

## 受け入れ条件 (Acceptance Criteria)
- [ ] C1RegressionTests 全 green、手動 UPDATE を含まない
- [ ] BcryptHashTests 全 green
- [ ] InitialAdminSeedTests 全 green

## 影響ファイル
- `D:\proj\agri-gis\tests\Tests\Bitemporal\C1RegressionTests.cs` (新規)
- `D:\proj\agri-gis\tests\Tests\Auth\BcryptHashTests.cs` (新規)
- `D:\proj\agri-gis\tests\Tests\Auth\InitialAdminSeedTests.cs` (新規)

## 実装ノート
```csharp
// C1RegressionTests.cs
[Fact]
public async Task SameDay_Update_Today_AsOf_Returns_NewCurrent_Only()
{
    var alice = _factory.WithActor("alice");
    var f = await alice.PostFeatureAsync(/* polygon A */);
    await alice.PatchFeatureAsync(f.entity_id, /* polygon B */);

    var asOfToday = await alice.GetAsync(
        $"/api/features?layer_id={f.layer_id}&as_of={DateTime.Today:yyyy-MM-dd}");
    var list = await asOfToday.Content.ReadFromJsonAsync<FeatureCollection>();
    var feat = list!.Features.Single(x => x.EntityId == f.entity_id);
    AssertGeomEquals(feat.Geometry, "polygon B");  // 新 current
}

[Fact]
public async Task SameDay_TwoUpdates_ZeroWidth_Doesnt_Break_AsOf()
{
    var alice = _factory.WithActor("alice");
    var f = await alice.PostFeatureAsync(/* A */);
    await alice.PatchFeatureAsync(f.entity_id, /* B */);
    await alice.PatchFeatureAsync(f.entity_id, /* C */);
    // 当日 asOf は最新 (C) のみ返す
    var res = await alice.GetAsync($"/api/features?as_of={DateTime.Today:yyyy-MM-dd}");
    var list = await res.Content.ReadFromJsonAsync<FeatureCollection>();
    AssertGeomEquals(list!.Features.Single().Geometry, "C");
}

// BcryptHashTests.cs
[Fact]
public async Task Admin_CreateUser_Hash_Uses_WorkFactor_11()
{
    var admin = _factory.WithActorAs("alice", "admin");
    await admin.PostAsync("/api/admin/users",
        JsonContent.Create(new { login_id = "x", display_name = "X", password = "pw1234567",
                                 org_id = SeedUsers.OrgId, roles = new[] { "general" } }));
    using var db = await OpenDb();
    var hash = await db.ExecuteScalarAsync<string>(
        "SELECT password_hash FROM users WHERE login_id='x'");
    Assert.Matches(@"^\$2[aby]\$11\$", hash);
}

// InitialAdminSeedTests.cs
[Fact]
public void Missing_AdminPw_Fails_Startup()
{
    Environment.SetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_PW", null);
    var ex = Assert.Throws<InvalidOperationException>(() =>
        new WebApplicationFactory<Program>().CreateClient());
    Assert.Contains("AGRI_GIS_INITIAL_ADMIN_PW", ex.Message);
}

[Fact]
public async Task Initial_Admin_Is_Upserted_On_Startup()
{
    Environment.SetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_PW", "init-pw-1");
    using var factory = new WebApplicationFactory<Program>();
    var client = factory.CreateClient();
    using var db = await OpenDb();
    var row = await db.QuerySingleAsync(
        "SELECT login_id FROM users WHERE login_id='admin' AND deleted_at IS NULL");
    Assert.NotNull(row);
}
```

注意点:
- InitialAdminSeedTests は `WebApplicationFactory` を独立に立ち上げる必要があり、他テストとの環境変数衝突を防ぐため `[Collection("AdminSeed")]` で直列化推奨
- C1RegressionTests の「過去日 INSERT」は migration 経由でしか作れないので、テスト DB に直接 `UPDATE feature_current SET valid_from = ...` で過去化（テスト用例外として OK、ただし手動補正ではない）

## テスト観点
- C1 修復の永続的回帰防止
- BCrypt 設定の自動確認
- 初期 admin seed の正確性
