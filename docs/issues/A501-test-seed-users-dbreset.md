# A501: SeedUsers fixture + DbReset で users/orgs seed

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 0.5d |
| Depends on | A101, A207 |
| Blocks | A502, A503, A504, A505, A506, A507, A508 |

## 概要
テスト用 `Fixtures/SeedUsers.cs` を新規追加し、`DbReset.RunAsync` で users / organizations / user_roles を seed する (alice=admin, bob=general, carol=guest)。

## 背景・目的
採択案「案 P」のテストセクション:
> `DbReset.RunAsync` で users/organizations を seed（alice=admin, bob=general, carol=guest）
> 新規 `Fixtures/SeedUsers.cs` に定数（`SeedUsers.Alice.UserId` 等）

## スコープ
### 含む
- `Tests/Fixtures/SeedUsers.cs`:
  - `SeedUsers.OrgId` (固定 int, e.g. 1)
  - `SeedUsers.Alice = new SeedUser(UserId, LoginId, DisplayName, Password, Roles)`
  - 同様に Bob, Carol
  - すべて UUID は固定値（テストでアサート可能に）
- `DbReset.RunAsync` に users/orgs/user_roles の TRUNCATE + INSERT を追加
- パスワードは事前計算 BCrypt hash を埋め込む（毎回 BCrypt 計算するとテスト遅くなる）
- もしくは work factor を低くしてテスト用 hash を作成（採択案 work factor 11 と整合させるなら計算済 hash 推奨）

### 含まない
- TokenForge (A502)
- ApiClientFactory.WithActorAs (A502)
- 既存テスト変更（A503）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `SeedUsers.Alice.UserId` 等の定数がテストで使える
- [ ] DbReset 後、users テーブルに 3 行（alice/bob/carol）
- [ ] user_roles に 3 行（alice=admin, bob=general, carol=guest）
- [ ] organizations に 1 行（id=1, code=`TEST_ORG`）
- [ ] alice の password で `/api/auth/login` が成功する hash が seed されている
- [ ] DbReset は冪等（複数回呼んでも seed 状態が同じ）

## 影響ファイル
- `D:\proj\agri-gis\tests\Fixtures\SeedUsers.cs` (新規)
- `D:\proj\agri-gis\tests\Fixtures\DbReset.cs` (修正)

## 実装ノート
```csharp
// Fixtures/SeedUsers.cs
public static class SeedUsers
{
    public const int OrgId = 1;
    public const string OrgCode = "TEST_ORG";

    public sealed record SeedUser(
        Guid UserId, string LoginId, string DisplayName, string Password, string[] Roles)
    {
        // BCrypt hash (work factor 11) 事前計算して埋め込む
        public string PasswordHash { get; init; } = "";
    }

    public static readonly SeedUser Alice = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "alice", "Alice Admin", "alice-pass",
        new[] { "admin" })
    {
        PasswordHash = "$2a$11$..." // 事前計算: BCrypt.Net.BCrypt.HashPassword("alice-pass", 11)
    };
    public static readonly SeedUser Bob = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "bob", "Bob General", "bob-pass",
        new[] { "general" })
    { PasswordHash = "$2a$11$..." };
    public static readonly SeedUser Carol = new(
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "carol", "Carol Guest", "carol-pass",
        new[] { "guest" })
    { PasswordHash = "$2a$11$..." };

    public static IEnumerable<SeedUser> All => new[] { Alice, Bob, Carol };
}

// Fixtures/DbReset.cs (追記)
public static async Task RunAsync(NpgsqlConnection db)
{
    // 既存の TRUNCATE 群 ...
    await db.ExecuteAsync("TRUNCATE user_roles, users, organizations RESTART IDENTITY CASCADE");

    await db.ExecuteAsync(
        "INSERT INTO organizations(id, name, code) OVERRIDING SYSTEM VALUE VALUES (@i, @n, @c)",
        new { i = SeedUsers.OrgId, n = "Test Org", c = SeedUsers.OrgCode });

    foreach (var u in SeedUsers.All)
    {
        await db.ExecuteAsync(@"
            INSERT INTO users(user_id, login_id, display_name, password_hash, org_id)
            VALUES (@id, @l, @d, @h, @o)",
            new { id = u.UserId, l = u.LoginId, d = u.DisplayName, h = u.PasswordHash, o = SeedUsers.OrgId });
        foreach (var r in u.Roles)
            await db.ExecuteAsync(
                "INSERT INTO user_roles(user_id, role) VALUES (@u, @r)",
                new { u = u.UserId, r });
    }
    // 既存の seed (layers 等) は維持
}
```

注意点:
- BCrypt hash は work factor 11 で事前計算した値をコード埋め込み（テスト実行時に毎回計算しない）
- 計算スクリプト例: `dotnet run` で `Console.WriteLine(BCrypt.Net.BCrypt.HashPassword("alice-pass", 11))`
- `OVERRIDING SYSTEM VALUE` で SERIAL に明示 id 指定

## テスト観点
- A504/A505/A506 すべての前提条件
- SeedUsers の hash が間違っていれば login が落ちる → A504 で検出
