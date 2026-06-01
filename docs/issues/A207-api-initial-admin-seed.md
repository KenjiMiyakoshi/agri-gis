# A207: 初期 admin の IHostedService による upsert

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | A101, A201 |
| Blocks | A508 |

## 概要
起動時に `AGRI_GIS_INITIAL_ADMIN_PW` 環境変数を読み、初期 admin ユーザを upsert する `IHostedService` を追加する。

## 背景・目的
採択案「案 P」の認証基盤セクション:
> 初期 admin: 環境変数 `AGRI_GIS_INITIAL_ADMIN_PW` → 起動時 `IHostedService` で upsert（無ければ起動失敗）

## スコープ
### 含む
- `Auth/InitialAdminSeeder.cs` (`IHostedService`)
- 環境変数 `AGRI_GIS_INITIAL_ADMIN_PW` (必須、無ければ起動失敗)
- 環境変数 `AGRI_GIS_INITIAL_ADMIN_LOGIN_ID` (オプション、デフォルト `admin`)
- 環境変数 `AGRI_GIS_INITIAL_ADMIN_ORG_CODE` (オプション、デフォルト `SYSTEM`)
- 起動時動作:
  1. `SYSTEM` org が無ければ INSERT
  2. admin user (login_id) が無ければ INSERT (password = BCrypt hash, work factor 11)
  3. 既存ならパスワードを毎回上書き (= rotation/緊急復旧パス)
  4. `user_roles(admin)` を ON CONFLICT DO NOTHING
- `Program.cs` で `AddHostedService<InitialAdminSeeder>()`

### 含まない
- 環境変数経由以外の admin 作成 UI（Admin CRUD で別 admin を作れる、A302）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `AGRI_GIS_INITIAL_ADMIN_PW` 未設定で起動 → 起動失敗、エラーメッセージ明示
- [ ] 設定済で初回起動 → users に admin 1 行、user_roles に admin role
- [ ] 既存 admin あり + パスワードを変えて再起動 → password_hash が更新される
- [ ] BCrypt work factor = 11
- [ ] SYSTEM org が無い場合自動作成
- [ ] 起動失敗時はアプリ停止 (StopApplication)

## 影響ファイル
- `D:\proj\agri-gis\api\Auth\InitialAdminSeeder.cs` (新規)
- `D:\proj\agri-gis\api\Program.cs`

## 実装ノート
```csharp
public sealed class InitialAdminSeeder : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<InitialAdminSeeder> _log;

    public InitialAdminSeeder(IServiceProvider sp, IHostApplicationLifetime lt, ILogger<InitialAdminSeeder> log)
    { _sp = sp; _lifetime = lt; _log = log; }

    public async Task StartAsync(CancellationToken ct)
    {
        var pw = Environment.GetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_PW");
        if (string.IsNullOrWhiteSpace(pw))
        {
            _log.LogCritical("AGRI_GIS_INITIAL_ADMIN_PW not set");
            _lifetime.StopApplication();
            throw new InvalidOperationException("AGRI_GIS_INITIAL_ADMIN_PW not set");
        }
        var loginId = Environment.GetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_LOGIN_ID") ?? "admin";
        var orgCode = Environment.GetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_ORG_CODE") ?? "SYSTEM";

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
        await db.OpenAsync(ct);
        using var tx = await db.BeginTransactionAsync(ct);

        var orgId = await db.ExecuteScalarAsync<int?>(
            "SELECT id FROM organizations WHERE code=@c AND deleted_at IS NULL", new { c = orgCode });
        if (orgId is null)
            orgId = await db.ExecuteScalarAsync<int>(
                "INSERT INTO organizations(name, code) VALUES (@n, @c) RETURNING id",
                new { n = orgCode, c = orgCode });

        var hash = BCrypt.Net.BCrypt.HashPassword(pw, workFactor: 11);
        var userId = await db.ExecuteScalarAsync<Guid?>(
            "SELECT user_id FROM users WHERE login_id=@l AND deleted_at IS NULL", new { l = loginId });
        if (userId is null)
        {
            userId = await db.ExecuteScalarAsync<Guid>(@"
                INSERT INTO users(login_id, display_name, password_hash, org_id)
                VALUES (@l, @l, @h, @o) RETURNING user_id",
                new { l = loginId, h = hash, o = orgId });
        }
        else
        {
            await db.ExecuteAsync(
                "UPDATE users SET password_hash=@h, updated_at=now() WHERE user_id=@u",
                new { h = hash, u = userId });
        }
        await db.ExecuteAsync(
            "INSERT INTO user_roles(user_id, role) VALUES (@u, 'admin') ON CONFLICT DO NOTHING",
            new { u = userId });
        await tx.CommitAsync(ct);
        _log.LogInformation("Initial admin '{login}' upserted in org '{org}'", loginId, orgCode);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

注意点:
- テスト環境で `IHostedService` を毎回走らせると BCrypt hash が遅い → A501 の DbReset で seed を別途行うため、テスト用 `AGRI_GIS_INITIAL_ADMIN_PW` は弱いものを使う
- A1xx の DDL に対する依存

## テスト観点
- A508 (InitialAdminSeedTests):
  - PW 未設定 → 起動失敗
  - 初回起動 → admin upsert
  - 2 回目起動 (PW 変更) → password_hash が更新される
  - 2 回目起動 (PW 同じ) → 冪等
