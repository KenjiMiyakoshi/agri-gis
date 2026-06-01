# 0301: `AgriGis.Api.Tests` プロジェクト立ち上げ + Testcontainers

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 1d |
| Depends on | 0101 |
| Blocks | 0302, 0303, 0304, 0305 |

## 概要
xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql` を使った API テストプロジェクトを立ち上げる。

## 背景・目的
案 B' は不変条件テスト（特に履歴 / 監査 / バージョン）を中核に据える。実 PostGIS が必要なので Testcontainers で `postgis/postgis:16-3.4` を起動する方針。

## スコープ
### 含む
- `api.tests/AgriGis.Api.Tests.csproj`
  - TargetFramework: `net8.0`
  - PackageReference:
    - `Microsoft.NET.Test.Sdk`
    - `xunit`
    - `xunit.runner.visualstudio`
    - `Microsoft.AspNetCore.Mvc.Testing` (8.0.x)
    - `Testcontainers.PostgreSql` (3.x)
    - `Npgsql` (8.0.3)
  - `ProjectReference` → `../api/AgriGis.Api.csproj`
- `Fixtures/PostgisContainerFixture.cs` (`IAsyncLifetime`, クラスフィクスチャ)
  - `postgis/postgis:16-3.4` を立てる
  - `db/init/001_init.sql` → `db/init/002_seed.sql` → `db/migration/*.sql` を順に流す
  - 接続文字列を公開
- `Fixtures/ApiFactory.cs` (`WebApplicationFactory<Program>`)
  - 接続文字列を環境変数 or 設定で上書き
- ソリューションファイル `AgriGis.sln` を新規作成（または既存に追加）して `api/`, `api.tests/` を含める
- スモークテスト 1 本: `GET /api/health` が 200

### 含まない
- 個別の不変条件テスト (0303)
- 楽観ロック / asOf 等 (0304)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `dotnet test` が PostGIS コンテナを起動して 1 テスト pass
- [ ] フィクスチャは collection でクラス間共有される
- [ ] テストごとにコンテナを再起動しない（共有）
- [ ] テスト用接続文字列は環境変数 `AGRI_GIS_DB_TEST` 等で確認可能

## 影響ファイル
- `D:\proj\agri-gis\api.tests\AgriGis.Api.Tests.csproj` (新規)
- `D:\proj\agri-gis\api.tests\Fixtures\PostgisContainerFixture.cs` (新規)
- `D:\proj\agri-gis\api.tests\Fixtures\ApiFactory.cs` (新規)
- `D:\proj\agri-gis\api.tests\HealthTests.cs` (新規, スモーク)
- `D:\proj\agri-gis\AgriGis.sln` (新規)

## 実装ノート
```csharp
// Fixtures/PostgisContainerFixture.cs
public sealed class PostgisContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:16-3.4")
            .WithDatabase("agri_gis")
            .WithUsername("agri_user")
            .WithPassword("agri_pass")
            .Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await RunSqlFile("db/init/001_init.sql");
        await RunSqlFile("db/init/002_seed.sql");
        foreach (var f in Directory.EnumerateFiles("db/migration", "*.sql").OrderBy(x => x))
            await RunSqlFile(f);
    }
    public Task DisposeAsync() => _container!.DisposeAsync().AsTask();

    private async Task RunSqlFile(string relPath)
    {
        var path = Path.Combine(SolutionRoot.Find(), relPath);
        var sql  = await File.ReadAllTextAsync(path);
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

// テスト本体は ICollectionFixture で共有
[CollectionDefinition("postgis")]
public sealed class PostgisCollection : ICollectionFixture<PostgisContainerFixture> { }
```

注意点:
- `db/init/002_seed.sql` を流す前に 001_init.sql のみだと layers カラムが旧形式。順序は **init → migration → seed の再投入** が正しい。本イシューでは「init → migration → seed の置換」ではなく、`002_seed.sql` を 0111 で新スキーマ対応版にしてあることが前提
- ソリューションルートの探し方は `SolutionRoot.Find()` を自作（`.git` か `AgriGis.sln` を上に向かって探す）

## テスト観点
- スモーク: `/api/health`
