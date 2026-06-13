using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace AgriGis.Api.Tests.Fixtures;

// PostGIS コンテナをクラスフィクスチャで共有起動。
// 起動時に db/init/001_init.sql → db/init/002_seed.sql → db/migration/*.sql を順に流す。
// 各テストは DbReset.RunAsync で行を初期化してから走る前提。
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
            // LGP106: 1 テストランで多数の ApiFactory (各々が独立 NpgsqlDataSource を持つ) を
            //   生成するため、PostgreSQL 既定の max_connections=100 では
            //   "53300: too many clients already" に到達しうる。上限を引き上げて余裕を持たせる。
            .WithCommand("-c", "max_connections=300")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // 順序: init (basic schema + seed) → migration (新カラム/関数を被せる)
        // ※ 002_seed.sql は既に新スキーマ前提の形になっているため、
        //    001_init で feature_current.created_by 列が無い状態で 002_seed を流すと失敗する。
        //    init の seed は後で TRUNCATE+再投入 (DbReset) で扱うので、ここでは 001_init のみ流して
        //    その後に migration 群 → 最後に 002_seed を流す。
        await RunSqlFile("db/init/001_init.sql");
        // 注: OrderBy(x => x) は既定で culture-aware で、Windows では `_` が `.` より小さく扱われ、
        // `0F03_org_layer_permission_backfill.sql` が `0F03_org_layer_permission.sql` より
        // 前に並んでしまう。ordinal 比較で「文字コード順」を強制する (Linux の `sort` と一致)。
        foreach (var f in Directory.EnumerateFiles(SolutionRoot.Resolve("db/migration"), "*.sql")
                                   .OrderBy(x => x, StringComparer.Ordinal))
        {
            await RunSqlFile(f, alreadyAbsolute: true);
        }
        await RunSqlFile("db/init/002_seed.sql");

        // layer_schema_version を layers から backfill (migration が空 layers 時に走るとシード行が無い)
        await ExecuteAsync(@"
            INSERT INTO layer_schema_version (layer_id, schema_version, schema_json, valid_from, valid_to, created_by)
            SELECT layer_id, schema_version, schema_json, now(), NULL, 'system'
              FROM layers
            ON CONFLICT (layer_id, schema_version) DO NOTHING;");
    }

    public Task DisposeAsync() => _container is null ? Task.CompletedTask : _container.DisposeAsync().AsTask();

    private async Task RunSqlFile(string path, bool alreadyAbsolute = false)
    {
        var fullPath = alreadyAbsolute ? path : SolutionRoot.Resolve(path);
        var sql = await File.ReadAllTextAsync(fullPath);
        try
        {
            await ExecuteAsync(sql);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"migration file failed: {Path.GetFileName(fullPath)}", ex);
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

// Testcontainers コンテナはテストクラス間で 1 つを使い回す。
[CollectionDefinition(Name)]
public sealed class PostgisCollection : ICollectionFixture<PostgisContainerFixture>
{
    public const string Name = "postgis";
}
